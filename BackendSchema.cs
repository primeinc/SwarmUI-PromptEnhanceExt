namespace PromptEnhance;

public static class BackendSchema
{
    public class MediaContent
    {
        public string Type { get; set; }
        public string Data { get; set; }
        public string MediaType { get; set; }
    }

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
