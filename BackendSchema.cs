namespace PromptEnhance;

/// <summary>
/// Builds the OpenAI-compatible `/v1/chat/completions` request body.
/// This is the single owned seam where the extension's inputs (settings,
/// prompt text, image parts) become the wire schema; nothing else in the
/// codebase shapes chat-completion JSON.
/// </summary>
public static class BackendSchema
{
    /// <summary>One image attachment, normalized by the frontend's image-context adapter.</summary>
    public class MediaContent
    {
        /// <summary>"base64" (data URI is synthesized here) or "url" (passed through as-is).</summary>
        public string Type { get; set; }

        public string Data { get; set; }

        /// <summary>MIME type for base64 parts; defaults to image/jpeg when the browser could not name one.</summary>
        public string MediaType { get; set; }
    }

    /// <summary>
    /// Assembles the messages array: optional system message (omitted when
    /// blank, since some servers reject empty system content), then a user
    /// message that is a plain string for text-only requests or an
    /// image_url+text content array when media is attached — the two shapes
    /// OpenAI-compatible servers accept for vision input.
    /// </summary>
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
