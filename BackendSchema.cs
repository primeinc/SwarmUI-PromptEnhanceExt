namespace PromptEnhance;

/// <summary>Builds request bodies for the single OpenAI-compatible chat-completions backend.
/// Pure builder — no I/O, no image processing, no logging — so it is trivial to read and reason about.
/// The shape follows the canonical OpenAI schema (openai-openapi/openapi.yaml,
/// ChatCompletionRequestUserMessage / ...MessageContentPartImage / ...MessageContentPartText):
///   { model, messages: [ {role:"system", content}, {role:"user", content} ], temperature, max_tokens }
/// where a user message's content is a plain string for text-only, or an array of
///   { type:"text", text } and { type:"image_url", image_url:{ url } } parts when an image is attached.</summary>
public static class BackendSchema
{
    /// <summary>A single image to attach to an enhance request.</summary>
    public class MediaContent
    {
        /// <summary>"base64" for inline data (sent as a data URI), or "url" for a direct image link.</summary>
        public string Type { get; set; }
        /// <summary>Base64 payload (no data-URI prefix) when <see cref="Type"/> is "base64", or the URL when "url".</summary>
        public string Data { get; set; }
        /// <summary>Source MIME type, e.g. "image/png", used to build the data URI. Defaults to image/jpeg when blank.</summary>
        public string MediaType { get; set; }
    }

    /// <summary>Builds the OpenAI-compatible chat-completions request body.</summary>
    /// <param name="model">Model id to send.</param>
    /// <param name="systemPrompt">Optional system message (the enhancement instruction). Omitted when blank.</param>
    /// <param name="userText">The user prompt text to enhance.</param>
    /// <param name="media">Optional images to attach as multimodal content. When non-empty the user message
    /// becomes a content array; otherwise it is a plain string.</param>
    /// <param name="temperature">Sampling temperature from settings.</param>
    /// <param name="maxTokens">Max response tokens from settings.</param>
    public static object BuildChatRequest(string model, string systemPrompt, string userText, List<MediaContent> media, double temperature, int maxTokens)
    {
        List<object> messages = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }
        bool hasImages = media is { Count: > 0 };
        if (hasImages)
        {
            List<object> parts = [];
            foreach (MediaContent m in media)
            {
                string url = m.Type == "base64"
                    ? $"data:{(string.IsNullOrWhiteSpace(m.MediaType) ? "image/jpeg" : m.MediaType)};base64,{m.Data}"
                    : m.Data;
                parts.Add(new { type = "image_url", image_url = new { url } });
            }
            parts.Add(new { type = "text", text = userText });
            messages.Add(new { role = "user", content = parts });
        }
        else
        {
            messages.Add(new { role = "user", content = userText });
        }
        return new
        {
            model,
            messages = messages.ToArray(),
            temperature,
            max_tokens = maxTokens,
            stream = false
        };
    }
}
