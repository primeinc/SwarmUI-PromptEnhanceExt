using System.Net;

namespace PromptEnhance.WebAPI;

/// <summary>
/// The extension's closed error taxonomy. Every backend failure the extension
/// can encounter is classified into exactly one of these categories; API
/// responses carry the snake_case code from <see cref="ErrorHandler.CategoryCode"/>
/// plus user-actionable text from <see cref="ErrorHandler.Format"/>.
/// </summary>
public enum PromptEnhanceErrorCategory
{
    ServerUnavailable,
    Timeout,
    InvalidBaseUrl,
    ModelMissing,
    UnsupportedImage,
    InvalidResponseShape,
    HttpError,
    Authentication,
    Generic
}

/// <summary>Maps raw failures (HTTP status codes, response bodies) into the classified taxonomy and user-facing text.</summary>
public static class ErrorHandler
{
    /// <summary>The stable wire identifier for a category. These codes are API contract; the frontend and tests pin them.</summary>
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

    /// <summary>
    /// User-facing recovery text for a category, optionally followed by an
    /// excerpt of the raw backend detail. Every message names the concrete
    /// action the user can take (fix the URL, pick a model, raise the timeout,
    /// disable image sending, ...) — classification without a recovery path
    /// would just be a fancier way to be broken.
    /// </summary>
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

    /// <summary>
    /// Classifies an HTTP non-success status. 404 maps to ModelMissing because
    /// OpenAI-compatible servers commonly 404 both unknown routes and unknown
    /// models; 401/403 map to Authentication; 5xx to ServerUnavailable;
    /// anything unrecognized stays a generic HttpError rather than guessing.
    /// </summary>
    public static PromptEnhanceErrorCategory CategorizeHttpStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PromptEnhanceErrorCategory.Authentication,
        HttpStatusCode.NotFound => PromptEnhanceErrorCategory.ModelMissing,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => PromptEnhanceErrorCategory.Timeout,
        HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity => PromptEnhanceErrorCategory.UnsupportedImage,
        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => PromptEnhanceErrorCategory.ServerUnavailable,
        _ => PromptEnhanceErrorCategory.HttpError
    };

    /// <summary>
    /// Heuristic for reclassifying a 400 on a request that carried media:
    /// OpenAI-compatible servers phrase image rejection inconsistently, so a
    /// body mentioning image/vision/multimodal is treated as UnsupportedImage.
    /// Only consulted when media was actually attached (see BackendClient).
    /// </summary>
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

    /// <summary>Caps raw backend text for safe inclusion in user-facing detail (default 600 chars, marked when truncated).</summary>
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
