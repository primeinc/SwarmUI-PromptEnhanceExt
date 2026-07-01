using System.Text.Json;

namespace PromptEnhance.Tests;

/// <summary>Covers <see cref="PromptEnhance.BackendSchema.BuildChatRequest"/> — the request-body shaper. The wire
/// shape must match the canonical OpenAI chat-completions schema (openai-openapi/openapi.yaml): a plain-string user
/// content for text, a content-array with <c>image_url</c> + <c>text</c> parts for multimodal, the system message
/// omitted when blank, and temperature/max_tokens carried through from settings.</summary>
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
        // Act
        JsonElement root = BuildRoot("m", "sys", "a cat", null);

        // Assert
        Xunit.Assert.Equal("m", root.GetProperty("model").GetString());
        JsonElement messages = root.GetProperty("messages");
        Xunit.Assert.Equal(2, messages.GetArrayLength());
        Xunit.Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Xunit.Assert.Equal("sys", messages[0].GetProperty("content").GetString());
        Xunit.Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Xunit.Assert.Equal("a cat", messages[1].GetProperty("content").GetString()); // plain string, not an array
    }

    [Xunit.Fact]
    public void BlankSystemPrompt_OmitsSystemMessage()
    {
        // Act
        JsonElement messages = BuildRoot("m", "  ", "hi", null).GetProperty("messages");

        // Assert
        Xunit.Assert.Equal(1, messages.GetArrayLength());
        Xunit.Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Xunit.Fact]
    public void WithBase64Image_ProducesMultimodalContentArray()
    {
        // Arrange
        List<BackendSchema.MediaContent> media = [new() { Type = "base64", Data = "QUJD", MediaType = "image/png" }];

        // Act
        JsonElement content = BuildRoot("m", "sys", "describe", media).GetProperty("messages")[1].GetProperty("content");

        // Assert — image part first, then the text part (matches the OpenAI content-part order the builder emits)
        Xunit.Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Xunit.Assert.Equal("image_url", content[0].GetProperty("type").GetString());
        Xunit.Assert.StartsWith("data:image/png;base64,QUJD", content[0].GetProperty("image_url").GetProperty("url").GetString());
        Xunit.Assert.Equal("text", content[1].GetProperty("type").GetString());
        Xunit.Assert.Equal("describe", content[1].GetProperty("text").GetString());
    }

    [Xunit.Fact]
    public void UrlImage_UsesUrlDirectly()
    {
        // Arrange
        List<BackendSchema.MediaContent> media = [new() { Type = "url", Data = "https://example.com/x.png" }];

        // Act
        string? url = BuildRoot("m", null, "d", media)
            .GetProperty("messages")[0].GetProperty("content")[0].GetProperty("image_url").GetProperty("url").GetString();

        // Assert
        Xunit.Assert.Equal("https://example.com/x.png", url);
    }

    [Xunit.Fact]
    public void TemperatureAndMaxTokens_FlowThroughFromSettings()
    {
        // Act
        JsonElement root = BuildRoot("m", "sys", "hi", null, temperature: 0.2, maxTokens: 42);

        // Assert
        Xunit.Assert.Equal(0.2, root.GetProperty("temperature").GetDouble(), precision: 3);
        Xunit.Assert.Equal(42, root.GetProperty("max_tokens").GetInt32());
    }
}
