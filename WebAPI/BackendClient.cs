using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using PromptEnhance.WebAPI.Models;

namespace PromptEnhance.WebAPI;

/// <summary>Backend transport for `GET /v1/models` and `POST /v1/chat/completions`, plus the reachability probe. Every failure returns a classified <see cref="PromptEnhanceErrorCategory"/> response.</summary>
[API.APIClass("PromptEnhance extension: calls to the user's configured OpenAI-compatible backend (model list and prompt enhancement).")]
public class BackendClient
{
    /// <summary>The one client for every backend call; see <see cref="CreateHttpClient"/>.</summary>
    internal static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>SwarmUI's <see cref="NetworkBackendUtils.MakeHttpClient"/> configuration with automatic redirects off, so a request never leaves the configured Base URL, and <see cref="ConnectToFirstAddressAsync"/> as the connect step. Per-request timeouts come from settings.</summary>
    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new(new SocketsHttpHandler() { PooledConnectionLifetime = TimeSpan.FromMinutes(10), MaxConnectionsPerServer = 1000, AllowAutoRedirect = false, ConnectCallback = ConnectToFirstAddressAsync });
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"SwarmUI/{Utilities.Version}");
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    /// <summary>Connects to every address the host resolves to at once and keeps the first that answers. The default connect tries them one after another, and Windows retries a refused SYN for about 2s per address, so a dead `localhost` (::1 and 127.0.0.1) outlasts the reachability probe.</summary>
    private static async ValueTask<Stream> ConnectToFirstAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        DnsEndPoint endPoint = context.DnsEndPoint;
        IPAddress[] addresses = IPAddress.TryParse(endPoint.Host, out IPAddress literal) ? [literal] : await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken);
        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }
        using CancellationTokenSource others = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        List<Task<Socket>> attempts = [.. addresses.Select(address => ConnectSocketAsync(new IPEndPoint(address, endPoint.Port), others.Token))];
        Exception firstFailure = null;
        while (attempts.Count > 0)
        {
            Task<Socket> finished = await Task.WhenAny(attempts);
            attempts.Remove(finished);
            try
            {
                Socket socket = await finished;
                others.Cancel();
                foreach (Task<Socket> loser in attempts)
                {
                    _ = loser.ContinueWith(t => t.Result.Dispose(), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
                }
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        ExceptionDispatchInfo.Throw(firstFailure);
        return null;
    }

    /// <summary>One TCP connect with Nagle off, as SocketsHttpHandler's own connect does; the socket is disposed on failure.</summary>
    private static async Task<Socket> ConnectSocketAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        Socket socket = new(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>A classified error for a 3xx response, naming where the backend tried to send the request; null for any other status.</summary>
    private static JObject RedirectError(HttpResponseMessage response)
    {
        int status = (int)response.StatusCode;
        if (status < 300 || status > 399)
        {
            return null;
        }
        string target = response.Headers.Location?.ToString() ?? "(no Location header)";
        return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.HttpError,
            $"The backend answered {status} redirecting to {target}. PromptEnhance does not follow redirects: set the Base URL to the address the server redirects to.");
    }

    /// <summary>How long the reachability probe waits for any response before letting the real call proceed.</summary>
    private const int ReachabilityTimeoutSeconds = 3;

    /// <summary>How long a "reachable" probe result is reused.</summary>
    private static readonly TimeSpan ReachabilityTtlSuccess = TimeSpan.FromSeconds(10);

    /// <summary>How long an "unreachable" probe result is reused.</summary>
    private static readonly TimeSpan ReachabilityTtlFailure = TimeSpan.FromSeconds(30);

    /// <summary>Probe results keyed by normalized Base URL.</summary>
    private static readonly MemoryCache ReachabilityCache = new(new MemoryCacheOptions());

    /// <summary>Drops the cached probe result for <paramref name="normalizedBase"/>, for when a backend is known to have started there.</summary>
    internal static void ForgetReachability(string normalizedBase)
    {
        ReachabilityCache.Remove(normalizedBase);
    }

    /// <summary>Normalizes a base URL: trims, strips trailing slashes and a trailing `/v1`, requires an absolute http(s) URI with no query, fragment, or user info (any of which would change the path or host the fixed `/v1/...` suffix reaches). Returns null otherwise.</summary>
    public static string NormalizeBaseUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        string trimmed = raw.Trim().TrimEnd('/');
        if (trimmed.Contains('?') || trimmed.Contains('#'))
        {
            return null;
        }
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^3].TrimEnd('/');
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }
        return trimmed;
    }

    private static string ModelsUrl(string normalizedBase) => $"{normalizedBase}/v1/models";

    private static string ChatUrl(string normalizedBase) => $"{normalizedBase}/v1/chat/completions";

    /// <summary>Reachability probe against `GET /v1/models` with a TTL cache (10s reachable, 30s unreachable). Sends no API key: any HTTP response, a 401 included, counts as reachable; only transport failures count as unreachable; a probe timeout counts as reachable.</summary>
    private static async Task<bool> IsReachable(string normalizedBase)
    {
        if (ReachabilityCache.TryGetValue(normalizedBase, out bool cached))
        {
            return cached;
        }
        bool reachable;
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(ReachabilityTimeoutSeconds));
            using HttpRequestMessage probe = new(HttpMethod.Get, ModelsUrl(normalizedBase));
            using HttpResponseMessage response = await HttpClient.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            reachable = true;
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            Logs.Warning($"[PromptEnhance] Reachability probe for {normalizedBase} got no response within {ReachabilityTimeoutSeconds}s; proceeding and letting the request timeout decide.");
            reachable = true;
        }
        catch (HttpRequestException ex)
        {
            Logs.Warning($"[PromptEnhance] Backend at {normalizedBase} not reachable: {ex.GetType().Name}");
            reachable = false;
        }
        ReachabilityCache.Set(normalizedBase, reachable, reachable ? ReachabilityTtlSuccess : ReachabilityTtlFailure);
        return reachable;
    }

    /// <summary>Per-request timeout from settings, clamped to [1, <see cref="SessionSettings.MaxTimeoutSeconds"/>].</summary>
    private static int ResolveTimeoutSeconds(JObject settings)
    {
        JToken token = settings["timeoutSeconds"];
        long raw = token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            ? token.Value<long>()
            : 60L;
        long clamped = Math.Clamp(raw, 1L, (long)SessionSettings.MaxTimeoutSeconds);
        return (int)clamped;
    }

    /// <summary>The error for a saved API key that cannot be sent as a header value. Never includes the key.</summary>
    private static JObject UnsendableKeyError() => PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Authentication,
        "The saved PromptEnhance API key contains spaces, line breaks, or non-ASCII characters, so it cannot be sent. Re-enter it under User → API Keys.");

    private static async Task<(JObject settings, string normalizedBase, string apiKey)> ResolveConfig(Session session, Action<JObject> setError)
    {
        JObject settingsResponse = await SessionSettings.GetPromptEnhanceSettings(session);
        if (settingsResponse["success"]?.Value<bool>() != true)
        {
            setError(settingsResponse);
            return (null, null, null);
        }
        JObject settings = settingsResponse["settings"] as JObject;
        string normalizedBase = NormalizeBaseUrl(settings?["baseUrl"]?.ToString());
        if (normalizedBase == null)
        {
            setError(PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidBaseUrl));
            return (null, null, null);
        }
        string apiKey = UpstreamApiKey.ForUser(session);
        if (apiKey != null && !UpstreamApiKey.IsSendable(apiKey))
        {
            setError(UnsendableKeyError());
            return (null, null, null);
        }
        return (settings, normalizedBase, apiKey);
    }

    /// <summary>API route: lists the backend's models.</summary>
    [API.APIDescription("Lists the models the configured backend offers, from its `GET /v1/models`. Sends the user's PromptEnhance API key, if set.",
        """
            "success": true,
            "models": [
                { "id": "llama3.2", "name": "llama3.2" }
            ]
            // on failure: "success": false, "error": "Cannot reach the LLM backend ...", "error_id": "server_unavailable"
            // error_id is one of: server_unavailable, timeout, invalid_base_url, model_missing, invalid_response_shape, http_error, authentication, generic
        """)]
    public static async Task<JObject> PromptEnhanceListModels(Session session)
    {
        JObject error = null;
        (JObject settings, string normalizedBase, string apiKey) = await ResolveConfig(session, e => error = e);
        if (error != null)
        {
            return error;
        }
        if (!await IsReachable(normalizedBase))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.ServerUnavailable);
        }
        return await ExecuteListModels(normalizedBase, ResolveTimeoutSeconds(settings), apiKey);
    }

    /// <summary>The raw `GET /v1/models` round-trip, sending `apiKey` as a bearer token when given.</summary>
    public static async Task<JObject> ExecuteListModels(string normalizedBase, int timeoutSec, string apiKey = null)
    {
        if (apiKey != null && !UpstreamApiKey.IsSendable(apiKey))
        {
            return UnsendableKeyError();
        }
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(timeoutSec));
            using HttpRequestMessage request = new(HttpMethod.Get, ModelsUrl(normalizedBase));
            UpstreamApiKey.Apply(request, apiKey);
            HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token);
            string body = await response.Content.ReadAsStringAsync();
            JObject redirect = RedirectError(response);
            if (redirect != null)
            {
                return redirect;
            }
            if (!response.IsSuccessStatusCode)
            {
                return PromptEnhanceAPI.CreateErrorResponse(ErrorHandler.CategorizeHttpStatus(response.StatusCode), PromptEnhanceAPI.ExtractErrorMessage(body));
            }
            List<ModelData> models = PromptEnhanceAPI.DeserializeModels(body);
            if (models == null)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidResponseShape, ErrorHandler.Excerpt(body));
            }
            return PromptEnhanceAPI.CreateModelsResponse(models);
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Timeout);
        }
        catch (HttpRequestException ex)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.ServerUnavailable, ex.Message);
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Unexpected error listing models: {ex.Message}");
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, ex.Message);
        }
    }

    /// <summary>The top-level keys a PromptEnhanceRun body may carry.</summary>
    private static readonly HashSet<string> RunBodyKeys = ["prompt", "swarmInput", "media"];

    private static bool IsLegacyMediaEntry(JToken entry)
    {
        if (entry is not JObject media
            || !media.TryGetValue("type", out JToken type)
            || type.Type != JTokenType.String
            || type.Value<string>() != "base64"
            || !media.TryGetValue("data", out JToken data)
            || data.Type != JTokenType.String
            || string.IsNullOrWhiteSpace(data.Value<string>())
            || !media.TryGetValue("mediaType", out JToken mediaType)
            || mediaType.Type != JTokenType.String
            || !MediaTypeHeaderValue.TryParse(mediaType.Value<string>(), out MediaTypeHeaderValue parsedMediaType)
            || !parsedMediaType.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || parsedMediaType.Parameters.Count > 0)
        {
            return false;
        }
        return true;
    }

    /// <summary>Checks the body shape before any session or settings access: `prompt` is a non-blank string, `swarmInput` (optional) is an object, and the legacy `media` array is accepted only without `swarmInput`. Returns null when valid, else an invalid_request error.</summary>
    public static JObject ValidateRunBody(JObject raw)
    {
        if (raw == null || !raw.TryGetValue("prompt", out JToken prompt) || prompt.Type != JTokenType.String || string.IsNullOrWhiteSpace(prompt.Value<string>()))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidRequest, "No prompt text was provided to enhance: `prompt` must be a non-blank string.");
        }
        foreach (JProperty property in raw.Properties())
        {
            if (!RunBodyKeys.Contains(property.Name))
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidRequest, $"'{property.Name}' is not a PromptEnhanceRun field. The body holds `prompt` and optionally `swarmInput` or legacy `media`.");
            }
        }
        if (raw.TryGetValue("swarmInput", out JToken swarmInput) && swarmInput is not JObject)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidRequest, "`swarmInput` must be an object.");
        }
        if (raw.TryGetValue("media", out JToken legacyMedia))
        {
            if (raw.ContainsKey("swarmInput"))
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidRequest, "`media` is only supported when `swarmInput` is omitted.");
            }
            if (legacyMedia is not JArray mediaEntries || mediaEntries.Any(entry => !IsLegacyMediaEntry(entry)))
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidRequest, "`media` must be an array of { type: 'base64', data: <base64 image bytes>, mediaType: <image MIME type> } objects.");
            }
        }
        return null;
    }

    /// <summary>Adds images from the legacy `media` request field to the multimodal context.</summary>
    private static void AddLegacyMedia(JArray mediaEntries, BackendSchema.PromptContext context)
    {
        for (int i = 0; i < mediaEntries.Count; i++)
        {
            JObject media = (JObject)mediaEntries[i]!;
            context.PromptImages.Add(new BackendSchema.MediaContent
            {
                Data = media["data"]!.Value<string>(),
                MediaType = media["mediaType"]!.Value<string>(),
                Label = $"Image {i + 1}"
            });
        }
    }

    /// <summary>Builds the request-supplied part of the enhance context from the user's settings: Prompt Images and the active model stack from <paramref name="swarmInput"/> through <see cref="SwarmContext.Resolve"/>. Throws <see cref="ArgumentException"/> for malformed input.
    /// Past Generations are added later by <see cref="AddPastGenerations"/>, once the cheap failure checks have passed.</summary>
    public static BackendSchema.PromptContext BuildContext(Session session, JObject settings, JObject swarmInput)
    {
        bool sendPromptImages = settings["sendPromptImages"].Value<bool>();
        bool sendActiveModelContext = settings["sendActiveModelContext"].Value<bool>();
        BackendSchema.PromptContext context = new();
        if (swarmInput == null)
        {
            if (sendPromptImages || sendActiveModelContext)
            {
                throw new ArgumentException("`swarmInput` is required while Send Prompt Images or Send Active Model Context is on.");
            }
        }
        else
        {
            SwarmContext.Resolve(session, swarmInput, sendPromptImages, sendActiveModelContext, context);
        }
        return context;
    }

    /// <summary>Adds the user's newest Past Generations to <paramref name="context"/> when the setting is above 0.</summary>
    public static void AddPastGenerations(Session session, JObject settings, BackendSchema.PromptContext context)
    {
        int pastGenerations = settings["pastGenerations"].Value<int>();
        if (pastGenerations > 0)
        {
            context.PastGenerations = GenerationHistory.Recent(session.User.UserID, pastGenerations);
        }
    }

    /// <summary>API route: the enhance call.</summary>
    [API.APIDescription("Sends a prompt, with the context channels enabled in the user's settings, to the configured backend's `POST /v1/chat/completions` with the user's system prompt and sampling settings, and returns the rewritten prompt. Sends the user's PromptEnhance API key, if set.",
        """
            "success": true,
            "response": "A weathered stone lighthouse on a rocky headland at dusk, ..."
            // on failure: "success": false, "error": "The request to the LLM backend timed out ...", "error_id": "timeout"
            // error_id is one of: server_unavailable, timeout, invalid_base_url, model_missing, unsupported_image, invalid_response_shape, http_error, authentication, invalid_request, generic
        """)]
    public static async Task<JObject> PromptEnhanceRun(Session session,
        [API.APIParameter("The request body: `prompt`, the non-blank prompt text to enhance; optionally `swarmInput`, SwarmUI generation input for enabled context channels; or legacy `media`, an array of { type: 'base64', data: <base64 image bytes>, mediaType: <image MIME type> } objects accepted only when `swarmInput` is omitted. `promptimages` while Send Prompt Images is on; `model`, `loras`, `loraweights`, `loratencweights`, `lorasectionconfinement` while Send Active Model Context is on. `swarmInput` is required while either is on. Past Generations come from the server-side history.")] JObject raw)
    {
        JObject bodyError = ValidateRunBody(raw);
        if (bodyError != null)
        {
            return bodyError;
        }
        string prompt = raw["prompt"].Value<string>();
        JObject error = null;
        (JObject settings, string normalizedBase, string apiKey) = await ResolveConfig(session, e => error = e);
        if (error != null)
        {
            return error;
        }
        BackendSchema.PromptContext context;
        try
        {
            context = BuildContext(session, settings, raw["swarmInput"] as JObject);
            if (raw["media"] is JArray legacyMedia)
            {
                AddLegacyMedia(legacyMedia, context);
            }
        }
        catch (ArgumentException ex)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidRequest, ex.Message);
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Could not build the enhance context: {ex}");
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, ex.Message);
        }
        string model = settings["model"].Value<string>();
        if (string.IsNullOrWhiteSpace(model))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.ModelMissing);
        }
        if (settings["pastGenerations"].Value<int>() > 0 && !GenerationHistory.IsOpen)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Past Generations is unavailable: the generation history store did not open on this server (see the server log). Set Past Generations to Include to 0 to enhance without it.");
        }
        if (!await IsReachable(normalizedBase))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.ServerUnavailable);
        }
        try
        {
            AddPastGenerations(session, settings, context);
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Could not read the Past Generations history: {ex}");
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, ex.Message);
        }
        return await ExecuteChat(normalizedBase, model, settings["systemPrompt"].Value<string>(), prompt, context, settings["temperature"].Value<double>(), settings["maxTokens"].Value<int>(), ResolveTimeoutSeconds(settings), apiKey);
    }

    /// <summary>The raw `POST /v1/chat/completions` round-trip, sending `apiKey` as a bearer token when given. A 400 on a request that carried images is reclassified as UnsupportedImage when <see cref="ErrorHandler.LooksLikeImageRejection"/> matches the body.</summary>
    public static async Task<JObject> ExecuteChat(string normalizedBase, string model, string systemPrompt, string userText, BackendSchema.PromptContext context, double temperature, int maxTokens, int timeoutSec, string apiKey = null)
    {
        if (apiKey != null && !UpstreamApiKey.IsSendable(apiKey))
        {
            return UnsendableKeyError();
        }
        bool hasImages = context.HasImages;
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(BackendSchema.BuildChatRequest(model, systemPrompt, userText, temperature, maxTokens, context));
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(timeoutSec));
            ByteArrayContent requestContent = new(json);
            requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using HttpRequestMessage request = new(HttpMethod.Post, ChatUrl(normalizedBase)) { Content = requestContent };
            UpstreamApiKey.Apply(request, apiKey);
            HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token);
            string body = await response.Content.ReadAsStringAsync();
            JObject redirect = RedirectError(response);
            if (redirect != null)
            {
                return redirect;
            }
            if (!response.IsSuccessStatusCode)
            {
                PromptEnhanceErrorCategory category = hasImages && response.StatusCode == HttpStatusCode.BadRequest && ErrorHandler.LooksLikeImageRejection(body)
                    ? PromptEnhanceErrorCategory.UnsupportedImage
                    : ErrorHandler.CategorizeHttpStatus(response.StatusCode);
                return PromptEnhanceAPI.CreateErrorResponse(category, PromptEnhanceAPI.ExtractErrorMessage(body));
            }
            string content = PromptEnhanceAPI.DeserializeChatContent(body);
            if (content == null)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidResponseShape, ErrorHandler.Excerpt(body));
            }
            return PromptEnhanceAPI.CreateSuccessResponse(content);
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Timeout);
        }
        catch (HttpRequestException ex)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.ServerUnavailable, ex.Message);
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Unexpected error during enhance: {ex.Message}");
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, ex.Message);
        }
    }
}
