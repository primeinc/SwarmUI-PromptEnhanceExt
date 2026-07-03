using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using PromptEnhance.WebAPI.Models;

namespace PromptEnhance.WebAPI;

/// <summary>
/// Backend transport: owns the two OpenAI-compatible seams
/// (`GET /v1/models`, `POST /v1/chat/completions`) plus the reachability
/// probe in front of them. Every failure leaves this class as a classified
/// <see cref="PromptEnhanceErrorCategory"/> response — no exception escapes
/// to SwarmUI's generic 500 handler.
/// </summary>
public class BackendClient
{
    /// <summary>
    /// Shared client from SwarmUI's own factory, with the per-client timeout
    /// disabled: timeouts are enforced per-request via CancellationTokenSource
    /// so the user's timeoutSeconds setting applies per call, not per client.
    /// </summary>
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = NetworkBackendUtils.MakeHttpClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    private const int ReachabilityTimeoutSeconds = 3;
    private static readonly TimeSpan ReachabilityTtlSuccess = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReachabilityTtlFailure = TimeSpan.FromSeconds(30);

    /// <summary>Prune horizon for the reachability cache; must stay comfortably above both TTLs so a still-live entry is never evicted.</summary>
    private static readonly TimeSpan ReachabilityPruneAge = TimeSpan.FromMinutes(5);
    private static readonly object ReachabilityLock = new();
    private static readonly Dictionary<string, (bool reachable, DateTime whenUtc)> ReachabilityCache = new();

    /// <summary>
    /// Normalizes a user-entered base URL: trims, strips trailing slashes and
    /// a trailing `/v1` (both root URLs and /v1 URLs are accepted in settings),
    /// and requires an absolute http(s) URI. Returns null for anything else —
    /// the caller classifies that as <see cref="PromptEnhanceErrorCategory.InvalidBaseUrl"/>.
    /// </summary>
    public static string NormalizeBaseUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        string trimmed = raw.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^3].TrimEnd('/');
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }
        return trimmed;
    }

    private static string ModelsUrl(string normalizedBase) => $"{normalizedBase}/v1/models";

    private static string ChatUrl(string normalizedBase) => $"{normalizedBase}/v1/chat/completions";

    /// <summary>
    /// Cheap reachability probe with a small TTL cache (10s reachable, 30s
    /// unreachable) so an offline backend fails fast and repeatedly clicking
    /// Enhance doesn't hammer a dead host. Probes `GET /v1/models` — the
    /// endpoint the real requests depend on — because some servers hang or
    /// refuse on the bare root while serving /v1/* fine. Any HTTP response —
    /// even an error page — counts as reachable; only transport failures
    /// (connection refused, DNS) count as unreachable. A probe timeout is
    /// inconclusive: a backend whose model listing is slow (an aggregating
    /// proxy, a cold model scan) must not be reported down when the real
    /// request could still succeed within the user's timeoutSeconds, so the
    /// probe gives up after <see cref="ReachabilityTimeoutSeconds"/> seconds
    /// and lets the per-request timeout govern. Each insert prunes entries
    /// older than <see cref="ReachabilityPruneAge"/> so the cache stays
    /// bounded over the process lifetime.
    /// </summary>
    private static async Task<bool> IsReachable(string normalizedBase)
    {
        lock (ReachabilityLock)
        {
            if (ReachabilityCache.TryGetValue(normalizedBase, out (bool reachable, DateTime whenUtc) cached))
            {
                TimeSpan ttl = cached.reachable ? ReachabilityTtlSuccess : ReachabilityTtlFailure;
                if (DateTime.UtcNow - cached.whenUtc < ttl)
                {
                    return cached.reachable;
                }
            }
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
        lock (ReachabilityLock)
        {
            DateTime nowUtc = DateTime.UtcNow;
            List<string> stale = [.. ReachabilityCache.Where(entry => nowUtc - entry.Value.whenUtc > ReachabilityPruneAge).Select(entry => entry.Key)];
            foreach (string key in stale)
            {
                ReachabilityCache.Remove(key);
            }
            ReachabilityCache[normalizedBase] = (reachable, nowUtc);
        }
        return reachable;
    }

    /// <summary>
    /// Effective per-request timeout from settings, clamped to
    /// [1, <see cref="SessionSettings.MaxTimeoutSeconds"/>] so an out-of-range
    /// stored value can never feed an invalid CancellationTokenSource duration.
    /// Reads as long first to avoid overflow on oversized stored integers.
    /// </summary>
    private static int ResolveTimeoutSeconds(JObject settings)
    {
        JToken token = settings["timeoutSeconds"];
        long raw = token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            ? token.Value<long>()
            : 60L;
        long clamped = Math.Clamp(raw, 1L, (long)SessionSettings.MaxTimeoutSeconds);
        return (int)clamped;
    }

    /// <summary>Loads the session's settings and validates the base URL, funneling failures through <paramref name="setError"/> as classified responses.</summary>
    private static async Task<(JObject settings, string normalizedBase)> ResolveConfig(Session session, Action<JObject> setError)
    {
        JObject settingsResponse = await SessionSettings.GetPromptEnhanceSettings(session);
        if (settingsResponse["success"]?.Value<bool>() != true)
        {
            setError(settingsResponse);
            return (null, null);
        }
        JObject settings = settingsResponse["settings"] as JObject;
        string normalizedBase = NormalizeBaseUrl(settings?["baseUrl"]?.ToString());
        if (normalizedBase == null)
        {
            setError(PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.InvalidBaseUrl));
            return (null, null);
        }
        return (settings, normalizedBase);
    }

    /// <summary>API route: lists the backend's models for the settings dropdown. Registered by <see cref="PromptEnhanceAPI.Register"/>.</summary>
    public static async Task<JObject> PromptEnhanceListModels(Session session)
    {
        JObject error = null;
        (JObject settings, string normalizedBase) = await ResolveConfig(session, e => error = e);
        if (error != null)
        {
            return error;
        }
        if (!await IsReachable(normalizedBase))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.ServerUnavailable);
        }
        return await ExecuteListModels(normalizedBase, ResolveTimeoutSeconds(settings));
    }

    /// <summary>
    /// The raw `GET /v1/models` round-trip, independent of session state so
    /// transport behavior is testable against a real socket (BackendTransportTests).
    /// Classification: HTTP non-success via <see cref="ErrorHandler.CategorizeHttpStatus"/>,
    /// unparseable JSON as InvalidResponseShape, cancellation as Timeout,
    /// connection failure as ServerUnavailable.
    /// </summary>
    public static async Task<JObject> ExecuteListModels(string normalizedBase, int timeoutSec)
    {
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(timeoutSec));
            using HttpRequestMessage request = new(HttpMethod.Get, ModelsUrl(normalizedBase));
            HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token);
            string body = await response.Content.ReadAsStringAsync();
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

    /// <summary>
    /// API route: the enhance call. Validates the prompt before touching any
    /// session state, resolves settings, requires a configured model, probes
    /// reachability, parses media, then delegates to <see cref="ExecuteChat"/>.
    /// Every early exit is a classified error response.
    /// </summary>
    public static async Task<JObject> PromptEnhanceRun(JObject rawInput, Session session)
    {
        string userText = rawInput?["prompt"]?.ToString();
        if (string.IsNullOrWhiteSpace(userText))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "No prompt text was provided to enhance.");
        }
        JObject error = null;
        (JObject settings, string normalizedBase) = await ResolveConfig(session, e => error = e);
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
            media = ParseMedia(rawInput?["media"] as JArray);
        }
        catch (ArgumentException ex)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.UnsupportedImage, ex.Message);
        }
        return await ExecuteChat(normalizedBase, model, systemPrompt, userText, media, temperature, maxTokens, timeoutSec);
    }

    /// <summary>
    /// The raw `POST /v1/chat/completions` round-trip (session-independent,
    /// socket-tested like <see cref="ExecuteListModels"/>). A 400 on a request
    /// that carried media is reclassified as UnsupportedImage when the body
    /// blames the image (<see cref="ErrorHandler.LooksLikeImageRejection"/>),
    /// so vision-incapable models produce an actionable error instead of a
    /// generic HTTP failure.
    /// </summary>
    public static async Task<JObject> ExecuteChat(string normalizedBase, string model, string systemPrompt, string userText, List<BackendSchema.MediaContent> media, double temperature, int maxTokens, int timeoutSec)
    {
        object requestBody = BackendSchema.BuildChatRequest(model, systemPrompt, userText, media, temperature, maxTokens);
        string json = JsonSerializer.Serialize(requestBody);
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(timeoutSec));
            using HttpRequestMessage request = new(HttpMethod.Post, ChatUrl(normalizedBase))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token);
            string body = await response.Content.ReadAsStringAsync();
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

    /// <summary>
    /// Boundary adapter for the request's media array. A present-but-dataless
    /// entry throws ArgumentException (classified upstream as UnsupportedImage)
    /// rather than silently sending a text-only request the user believes
    /// included their image.
    /// </summary>
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
