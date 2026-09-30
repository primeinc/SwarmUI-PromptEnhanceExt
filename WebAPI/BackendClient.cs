using System.Net;
using System.Net.Http;
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
    private static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>SwarmUI's <see cref="NetworkBackendUtils.MakeHttpClient"/> configuration with automatic redirects off, so a request never leaves the configured Base URL. Per-request timeouts come from settings.</summary>
    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new(new SocketsHttpHandler() { PooledConnectionLifetime = TimeSpan.FromMinutes(10), MaxConnectionsPerServer = 1000, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"SwarmUI/{Utilities.Version}");
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
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
    private static readonly HashSet<string> RunBodyKeys = ["prompt", "swarmInput"];

    /// <summary>Checks the body shape before any session or settings access. Returns null when valid, else an invalid_request error.</summary>
    public static JObject ValidateRunBody(string prompt, JObject raw)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidRequest, "No prompt text was provided to enhance.");
        }
        foreach (JProperty property in raw.Properties())
        {
            if (!RunBodyKeys.Contains(property.Name))
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidRequest, $"'{property.Name}' is not a PromptEnhanceRun field. The body holds `prompt` and optionally `swarmInput`.");
            }
        }
        if (raw.TryGetValue("swarmInput", out JToken swarmInput) && swarmInput is not JObject)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidRequest, "`swarmInput` must be an object.");
        }
        return null;
    }

    /// <summary>Builds the context an enhance request sends, from the user's settings: Prompt Images and the active model stack from <paramref name="swarmInput"/> through <see cref="SwarmContext.Resolve"/>, Past Generations from <see cref="GenerationHistory.Recent"/>. Throws <see cref="ArgumentException"/> for malformed input.</summary>
    public static BackendSchema.PromptContext BuildContext(Session session, JObject settings, JObject swarmInput)
    {
        bool sendPromptImages = settings["sendPromptImages"].Value<bool>();
        bool sendActiveModelContext = settings["sendActiveModelContext"].Value<bool>();
        int pastGenerations = settings["pastGenerations"].Value<int>();
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
        if (pastGenerations > 0)
        {
            context.PastGenerations = GenerationHistory.Recent(session.User.UserID, pastGenerations);
        }
        return context;
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
        [API.APIParameter("The prompt text to enhance.")] string prompt,
        [API.APIParameter("The whole request body. Besides `prompt` it may hold only `swarmInput`: SwarmUI generation input, keyed by the Generate tab's parameter ids, for the enabled context channels. `promptimages` while Send Prompt Images is on; `model`, `loras`, `loraweights`, `loratencweights`, `lorasectionconfinement` while Send Active Model Context is on. Required while either is on. Past Generations come from the server-side history.")] JObject raw)
    {
        JObject bodyError = ValidateRunBody(prompt, raw);
        if (bodyError != null)
        {
            return bodyError;
        }
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
        if (!await IsReachable(normalizedBase))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.ServerUnavailable);
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
        object requestBody = BackendSchema.BuildChatRequest(model, systemPrompt, userText, temperature, maxTokens, context);
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
                PromptEnhanceErrorCategory category = context.HasImages && response.StatusCode == HttpStatusCode.BadRequest && ErrorHandler.LooksLikeImageRejection(body)
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
