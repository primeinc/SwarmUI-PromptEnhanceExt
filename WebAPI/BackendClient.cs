using System.IO;
using System.Net;
using System.Net.Http;
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

    /// <summary>API route: the enhance call.</summary>
    [API.APIDescription("Sends a prompt, and optionally images, to the configured backend's `POST /v1/chat/completions` with the user's system prompt and sampling settings, and returns the rewritten prompt. Sends the user's PromptEnhance API key, if set.",
        """
            "success": true,
            "response": "A weathered stone lighthouse on a rocky headland at dusk, ..."
            // on failure: "success": false, "error": "The request to the LLM backend timed out ...", "error_id": "timeout"
            // error_id is one of: server_unavailable, timeout, invalid_base_url, model_missing, unsupported_image, invalid_response_shape, http_error, authentication, generic
        """)]
    public static async Task<JObject> PromptEnhanceRun(
        [API.APIParameter("The request body: `prompt` (string, required, the text to enhance) and optional `media`, an array of { type: 'base64', data: <base64 image bytes>, mediaType: 'image/png' or similar } sent to the model as images.")] JObject raw,
        Session session)
    {
        string userText = raw?["prompt"]?.ToString();
        if (string.IsNullOrWhiteSpace(userText))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "No prompt text was provided to enhance.");
        }
        JObject error = null;
        (JObject settings, string normalizedBase, string apiKey) = await ResolveConfig(session, e => error = e);
        if (error != null)
        {
            return error;
        }
        string model = settings["model"]?.ToString();
        if (string.IsNullOrWhiteSpace(model))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.ModelMissing);
        }
        if (!await IsReachable(normalizedBase))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.ServerUnavailable);
        }
        string systemPrompt = settings["systemPrompt"]?.ToString();
        double temperature = settings["temperature"]?.Value<double?>() ?? 0.7;
        int maxTokens = settings["maxTokens"]?.Value<int?>() ?? 1024;
        int timeoutSec = ResolveTimeoutSeconds(settings);
        List<BackendSchema.MediaContent> media;
        try
        {
            media = ParseMedia(raw?["media"] as JArray);
        }
        catch (ArgumentException ex)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.UnsupportedImage, ex.Message);
        }
        return await ExecuteChat(normalizedBase, model, systemPrompt, userText, media, temperature, maxTokens, timeoutSec, apiKey);
    }

    /// <summary>The raw `POST /v1/chat/completions` round-trip, sending `apiKey` as a bearer token when given. A 400 on a request that carried media is reclassified as UnsupportedImage when <see cref="ErrorHandler.LooksLikeImageRejection"/> matches the body.</summary>
    public static async Task<JObject> ExecuteChat(string normalizedBase, string model, string systemPrompt, string userText, List<BackendSchema.MediaContent> media, double temperature, int maxTokens, int timeoutSec, string apiKey = null)
    {
        if (apiKey != null && !UpstreamApiKey.IsSendable(apiKey))
        {
            return UnsendableKeyError();
        }
        object requestBody = BackendSchema.BuildChatRequest(model, systemPrompt, userText, media, temperature, maxTokens);
        string json = JsonSerializer.Serialize(requestBody);
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(timeoutSec));
            using HttpRequestMessage request = new(HttpMethod.Post, ChatUrl(normalizedBase))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
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
                PromptEnhanceErrorCategory category = media is { Count: > 0 } && response.StatusCode == HttpStatusCode.BadRequest && ErrorHandler.LooksLikeImageRejection(body)
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

    /// <summary>Parses the request's media array. A present-but-dataless entry throws ArgumentException.</summary>
    public static List<BackendSchema.MediaContent> ParseMedia(JArray media)
    {
        List<BackendSchema.MediaContent> result = [];
        if (media == null)
        {
            return result;
        }
        foreach (JToken item in media)
        {
            string data = item["data"]?.ToString();
            if (string.IsNullOrWhiteSpace(data))
            {
                throw new ArgumentException("A media entry was attached but carried no image data.");
            }
            result.Add(new BackendSchema.MediaContent
            {
                Type = item["type"]?.ToString() ?? "base64",
                Data = data,
                MediaType = item["mediaType"]?.ToString()
            });
        }
        return result;
    }
}
