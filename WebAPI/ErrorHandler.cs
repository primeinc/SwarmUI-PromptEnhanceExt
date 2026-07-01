using System.Net;

namespace PromptEnhance.WebAPI;

/// <summary>The structured error categories this extension can surface. Every failure the enhance/model-list path
/// can produce maps to exactly one of these, so the UI can react per-category instead of parsing free text.</summary>
public enum PromptEnhanceErrorCategory
{
    /// <summary>The backend server could not be reached (connection refused, DNS failure, reachability probe failed).</summary>
    ServerUnavailable,
    /// <summary>The request exceeded the configured timeout and was cancelled.</summary>
    Timeout,
    /// <summary>The configured base URL is empty or not a valid absolute http(s) URL.</summary>
    InvalidBaseUrl,
    /// <summary>No model was selected, or the backend returned 404 Not Found (typically an unknown or unloaded model).</summary>
    ModelMissing,
    /// <summary>An image was attached but the backend/model rejected it (no vision support, bad format).</summary>
    UnsupportedImage,
    /// <summary>The response was not valid JSON, or did not match the expected chat-completions shape.</summary>
    InvalidResponseShape,
    /// <summary>The backend returned a non-success HTTP status. The response body excerpt is included.</summary>
    HttpError,
    /// <summary>The backend rejected the request as unauthorized (missing/invalid API key).</summary>
    Authentication,
    /// <summary>Any error that does not fit a more specific category.</summary>
    Generic
}

/// <summary>Produces structured, user-facing error payloads for the single OpenAI-compatible backend.
/// Callers return <see cref="PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory, string)"/> rather than
/// throwing, so a thrown exception is never the UI contract.</summary>
public static class ErrorHandler
{
    /// <summary>Stable machine-readable code for a category (sent to the UI as <c>errorCategory</c>).</summary>
    public static string CategoryCode(PromptEnhanceErrorCategory category) => category switch
    {
        PromptEnhanceErrorCategory.ServerUnavailable => "server_unavailable",
        PromptEnhanceErrorCategory.Timeout => "timeout",
        PromptEnhanceErrorCategory.InvalidBaseUrl => "invalid_base_url",
        PromptEnhanceErrorCategory.ModelMissing => "model_missing",
        PromptEnhanceErrorCategory.UnsupportedImage => "unsupported_image",
        PromptEnhanceErrorCategory.InvalidResponseShape => "invalid_response_shape",
        PromptEnhanceErrorCategory.HttpError => "http_error",
        PromptEnhanceErrorCategory.Authentication => "authentication",
        _ => "generic"
    };

    /// <summary>A concise, actionable message for a category. <paramref name="detail"/> (e.g. an exception message or a
    /// response-body excerpt) is appended when present.</summary>
    public static string Format(PromptEnhanceErrorCategory category, string detail = null)
    {
        string baseMessage = category switch
        {
            PromptEnhanceErrorCategory.ServerUnavailable =>
                "Cannot reach the LLM backend. Make sure the server is running and the Base URL in PromptEnhance settings is correct.",
            PromptEnhanceErrorCategory.Timeout =>
                "The request to the LLM backend timed out. Try a smaller prompt, a faster model, or raise the timeout in settings.",
            PromptEnhanceErrorCategory.InvalidBaseUrl =>
                "The Base URL in PromptEnhance settings is empty or not a valid http(s) URL (for example http://localhost:11434).",
            PromptEnhanceErrorCategory.ModelMissing =>
                "No usable model. Pick a model in PromptEnhance settings, and confirm the backend has it loaded.",
            PromptEnhanceErrorCategory.UnsupportedImage =>
                "The selected model rejected the attached image. Use a vision-capable model, or turn off 'Send selected image' for this enhance.",
            PromptEnhanceErrorCategory.InvalidResponseShape =>
                "The backend returned a response that was not valid OpenAI-style chat JSON. Confirm the Base URL points at an OpenAI-compatible server.",
            PromptEnhanceErrorCategory.HttpError =>
                "The LLM backend returned an error response.",
            PromptEnhanceErrorCategory.Authentication =>
                "The LLM backend rejected the request as unauthorized. This extension sends no API key; point the Base URL at an OpenAI-compatible server that does not require authentication (a local server such as Ollama, LM Studio, or llama.cpp).",
            _ => "Something went wrong talking to the LLM backend."
        };
        return string.IsNullOrWhiteSpace(detail) ? baseMessage : $"{baseMessage}\n\nDetail: {Excerpt(detail)}";
    }

    /// <summary>Maps a non-success HTTP status to a category. 401/403 -&gt; authentication, 404 -&gt; model missing,
    /// 408/504 -&gt; timeout, 413/422 -&gt; unsupported image (payload/validation), 5xx -&gt; server unavailable,
    /// everything else -&gt; generic HTTP error.</summary>
    public static PromptEnhanceErrorCategory CategorizeHttpStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PromptEnhanceErrorCategory.Authentication,
        HttpStatusCode.NotFound => PromptEnhanceErrorCategory.ModelMissing,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => PromptEnhanceErrorCategory.Timeout,
        HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity => PromptEnhanceErrorCategory.UnsupportedImage,
        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => PromptEnhanceErrorCategory.ServerUnavailable,
        _ => PromptEnhanceErrorCategory.HttpError
    };

    /// <summary>Whether an error body plausibly blames the attached image (no vision support, bad image format) rather
    /// than an unrelated bad request. The OpenAI error envelope carries a human-readable <c>message</c> (plus
    /// <c>type</c>/<c>code</c>); this scans the raw body for image/vision terms so a bare 400 is never mislabeled
    /// <see cref="PromptEnhanceErrorCategory.UnsupportedImage"/>.</summary>
    public static bool LooksLikeImageRejection(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }
        return body.Contains("image", StringComparison.OrdinalIgnoreCase)
            || body.Contains("vision", StringComparison.OrdinalIgnoreCase)
            || body.Contains("multimodal", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Truncates long detail text (e.g. a raw response body) so error payloads stay small.</summary>
    public static string Excerpt(string text, int max = 600)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        text = text.Trim();
        return text.Length <= max ? text : string.Concat(text.AsSpan(0, max), " …(truncated)");
    }
}
