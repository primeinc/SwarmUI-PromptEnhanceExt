using System.Text.Json;

namespace PromptEnhance.Tests;

public class BackendSchemaTests
{
    private static JsonElement BuildRoot(string model, string? system, string user,
        List<BackendSchema.MediaContent>? media, double temperature = 0.7, int maxTokens = 1024)
    {
        object body = BackendSchema.BuildChatRequest(model, system!, user, media!, temperature, maxTokens);
        return JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement;
    }

    [Xunit.Fact]
    public void TextOnly_UsesPlainStringUserContent()
    {
        JsonElement root = BuildRoot("m", "sys", "a cat", null);

        Xunit.Assert.Equal("m", root.GetProperty("model").GetString());
        JsonElement messages = root.GetProperty("messages");
        Xunit.Assert.Equal(2, messages.GetArrayLength());
        Xunit.Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Xunit.Assert.Equal("sys", messages[0].GetProperty("content").GetString());
        Xunit.Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Xunit.Assert.Equal("a cat", messages[1].GetProperty("content").GetString());
    }

    [Xunit.Fact]
    public void BlankSystemPrompt_OmitsSystemMessage()
    {
        JsonElement messages = BuildRoot("m", "  ", "hi", null).GetProperty("messages");

        Xunit.Assert.Equal(1, messages.GetArrayLength());
        Xunit.Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Xunit.Fact]
    public void WithBase64Image_ProducesMultimodalContentArray()
    {
        List<BackendSchema.MediaContent> media = [new() { Type = "base64", Data = "QUJD", MediaType = "image/png" }];

        JsonElement content = BuildRoot("m", "sys", "describe", media).GetProperty("messages")[1].GetProperty("content");

        Xunit.Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Xunit.Assert.Equal("image_url", content[0].GetProperty("type").GetString());
        Xunit.Assert.StartsWith("data:image/png;base64,QUJD", content[0].GetProperty("image_url").GetProperty("url").GetString());
        Xunit.Assert.Equal("text", content[1].GetProperty("type").GetString());
        Xunit.Assert.Equal("describe", content[1].GetProperty("text").GetString());
    }

    [Xunit.Fact]
    public void UrlImage_UsesUrlDirectly()
    {
        List<BackendSchema.MediaContent> media = [new() { Type = "url", Data = "https://example.com/x.png" }];

        string? url = BuildRoot("m", null, "d", media)
            .GetProperty("messages")[0].GetProperty("content")[0].GetProperty("image_url").GetProperty("url").GetString();

        Xunit.Assert.Equal("https://example.com/x.png", url);
    }

    [Xunit.Fact]
    public void TemperatureAndMaxTokens_FlowThroughFromSettings()
    {
        JsonElement root = BuildRoot("m", "sys", "hi", null, temperature: 0.2, maxTokens: 42);

        Xunit.Assert.Equal(0.2, root.GetProperty("temperature").GetDouble(), precision: 3);
        Xunit.Assert.Equal(42, root.GetProperty("max_tokens").GetInt32());
    }
}
