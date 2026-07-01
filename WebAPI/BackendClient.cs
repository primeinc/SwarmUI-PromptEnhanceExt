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

/// <summary>The single OpenAI-compatible backend client. Owns exactly two transport seams:
/// <c>GET {base}/v1/models</c> and <c>POST {base}/v1/chat/completions</c>. Every failure is returned as a structured
/// error payload (never thrown to the UI). The base URL is normalized so users may enter either a server root or a
/// URL ending in <c>/v1</c>.</summary>
public class BackendClient
{
    // Infinite client timeout; each request supplies its own CancellationTokenSource so the settings timeout is authoritative.
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
    private static readonly object ReachabilityLock = new();
    /// <summary>Reachability results keyed by normalized base URL. Same-key re-probes are refreshed in place (see the TTL
    /// write below), so entries accumulate only across <em>distinct</em> base URLs. The base URL comes from settings as a
    /// single value, giving an effective cardinality of one, so this cache is intentionally not evicted.</summary>
    private static readonly Dictionary<string, (bool reachable, DateTime whenUtc)> ReachabilityCache = new();

    /// <summary>Normalizes a user-entered base URL to a bare server root: trims whitespace and any trailing slash,
    /// and strips a trailing <c>/v1</c> so the owned seams resolve cleanly. Returns null when the value is not a valid
    /// absolute http(s) URL.</summary>
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

    /// <summary>Fast connectivity probe against the server root, cached (10s success / 30s failure) to avoid hammering a
    /// dead server. Any HTTP response counts as reachable; only connection failure/timeout counts as unreachable.</summary>
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
            using HttpRequestMessage probe = new(HttpMethod.Get, normalizedBase);
            await HttpClient.SendAsync(probe, cts.Token);
            reachable = true;
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or HttpRequestException)
        {
            Logs.Warning($"[PromptEnhance] Backend at {normalizedBase} not reachable: {ex.GetType().Name}");
            reachable = false;
        }
        lock (ReachabilityLock)
        {
            ReachabilityCache[normalizedBase] = (reachable, DateTime.UtcNow);
        }
        return reachable;
    }

    /// <summary>Resolves the current settings and the normalized base URL, returning a ready-to-use tuple or a
    /// structured error payload via <paramref name="setError"/>.</summary>
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

    /// <summary>API route: list models from <c>GET {base}/v1/models</c>.</summary>
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
        int timeoutSec = settings["timeoutSeconds"]?.Value<int?>() ?? 60;
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

    /// <summary>API route: enhance a prompt via <c>POST {base}/v1/chat/completions</c>.
    /// Input: <c>{ "prompt": string, "media": [ { "type", "data", "mediaType" } ] (optional) }</c>.
    /// The model, system prompt, temperature, max tokens, and timeout all come from settings (single source of truth).</summary>
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
        int timeoutSec = settings["timeoutSeconds"]?.Value<int?>() ?? 60;
        List<BackendSchema.MediaContent> media;
        try
        {
            media = ParseMedia(rawInput?["media"] as JArray);
        }
        catch (ArgumentException ex)
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.UnsupportedImage, ex.Message);
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
            HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                // A 400 with an image attached is "unsupported image" only when the backend error body actually blames
                // the image; a bare 400 is far more often an ordinary bad request and must not be mislabeled.
                PromptEnhanceErrorCategory category = media.Count > 0 && response.StatusCode == HttpStatusCode.BadRequest && ErrorHandler.LooksLikeImageRejection(body)
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

    /// <summary>Parses the optional <c>media</c> array into image parts. A present-but-malformed entry (missing or
    /// blank <c>data</c>) throws <see cref="ArgumentException"/> rather than being silently dropped, so the caller can
    /// surface a categorized error instead of downgrading to a text-only request behind the user's back. A null array
    /// (no image requested) is not a failure and returns an empty list.</summary>
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
