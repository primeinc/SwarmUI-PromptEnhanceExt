using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using PromptEnhance.WebAPI.Models;

namespace PromptEnhance.WebAPI;

/// <summary>The extension's closed error taxonomy. API responses carry the snake_case code from <see cref="ErrorHandler.CategoryCode"/> plus text from <see cref="ErrorHandler.Format"/>.</summary>
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

    /// <summary>User-facing recovery text for a category, optionally followed by an excerpt of the raw backend detail.</summary>
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

    /// <summary>Classifies an HTTP non-success status: 401/403 → Authentication, 404 → ModelMissing, 408/504 → Timeout, 413/422 → UnsupportedImage, 500/502/503 → ServerUnavailable, else HttpError.</summary>
    public static PromptEnhanceErrorCategory CategorizeHttpStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PromptEnhanceErrorCategory.Authentication,
        HttpStatusCode.NotFound => PromptEnhanceErrorCategory.ModelMissing,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => PromptEnhanceErrorCategory.Timeout,
        HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity => PromptEnhanceErrorCategory.UnsupportedImage,
        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => PromptEnhanceErrorCategory.ServerUnavailable,
        _ => PromptEnhanceErrorCategory.HttpError
    };

    private static readonly Regex ImageRejectionPattern = new(@"(?<![a-zA-Z0-9])(?:images?|visions?|multimodal|image_url)(?![a-zA-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Case-insensitive deserialization options shared by every OpenAI-compatible wire-shape parse in the extension.</summary>
    internal static readonly JsonSerializerOptions WireOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Token-scans an error body for image/vision/multimodal terms. When the body is an OpenAI error envelope only error.message, error.type, and error.code are scanned; the raw body is scanned only when the envelope shape is absent.</summary>
    public static bool LooksLikeImageRejection(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }
        ChatError envelope = TryParseErrorEnvelope(body);
        return ImageRejectionPattern.IsMatch(envelope == null ? body : $"{envelope.Message}\n{envelope.Type}\n{envelope.Code}");
    }

    /// <summary>Parses the body as an OpenAI-style error envelope (`{"error":{...}}`), returning the inner error object when present, else null.</summary>
    internal static ChatError TryParseErrorEnvelope(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<ChatErrorResponse>(body, WireOptions)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Caps raw backend text for inclusion in user-facing detail (default 600 chars, marked when truncated).</summary>
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
