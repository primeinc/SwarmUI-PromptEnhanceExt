using System.Text.Json;

namespace PromptEnhance.Tests;

public class BackendSchemaTests
{
    private static JsonElement BuildRoot(string model, string? system, string user, BackendSchema.PromptContext? context = null, double temperature = 0.7, int maxTokens = 1024)
    {
        object body = BackendSchema.BuildChatRequest(model, system!, user, temperature, maxTokens, context ?? new());
        return JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement;
    }

    /// <summary>The user message's parts as (type, text-or-url) pairs, in order.</summary>
    private static List<(string Type, string Value)> Parts(BackendSchema.PromptContext context)
    {
        List<(string, string)> parts = [];
        foreach (JsonElement part in BuildRoot("m", "sys", "CURRENT", context).GetProperty("messages")[1].GetProperty("content").EnumerateArray())
        {
            string type = part.GetProperty("type").GetString()!;
            parts.Add((type, type == "text" ? part.GetProperty("text").GetString()! : part.GetProperty("image_url").GetProperty("url").GetString()!));
        }
        return parts;
    }

    [Xunit.Fact]
    public void EmptyContext_UsesPlainStringUserContent()
    {
        JsonElement root = BuildRoot("m", "sys", "a cat");

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
        JsonElement messages = BuildRoot("m", "  ", "hi").GetProperty("messages");

        Xunit.Assert.Equal(1, messages.GetArrayLength());
        Xunit.Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Xunit.Fact]
    public void TemperatureAndMaxTokens_FlowThroughFromSettings()
    {
        JsonElement root = BuildRoot("m", "sys", "hi", temperature: 0.2, maxTokens: 42);

        Xunit.Assert.Equal(0.2, root.GetProperty("temperature").GetDouble(), precision: 3);
        Xunit.Assert.Equal(42, root.GetProperty("max_tokens").GetInt32());
    }

    [Xunit.Fact]
    public void PromptImages_KeepOrderAndLabelEachImageRightBeforeIt()
    {
        List<(string Type, string Value)> parts = Parts(new()
        {
            PromptImages =
            [
                new() { Data = "QQ==", MediaType = "image/png", Label = "Image 1" },
                new() { Data = "Qg==", MediaType = "image/jpeg", Label = "Image 2" }
            ]
        });

        int first = parts.IndexOf(("text", "Image 1"));
        int second = parts.IndexOf(("text", "Image 2"));
        Xunit.Assert.True(first > 0 && second > first);
        Xunit.Assert.Equal(("image_url", "data:image/png;base64,QQ=="), parts[first + 1]);
        Xunit.Assert.Equal(("image_url", "data:image/jpeg;base64,Qg=="), parts[second + 1]);
        Xunit.Assert.Equal(("text", "CURRENT PROMPT TO ENHANCE:\nCURRENT"), parts[^1]);
    }

    [Xunit.Fact]
    public void PastGenerations_LabelEveryOutputWithItsOwnPromptAndMetadata()
    {
        List<(string Type, string Value)> parts = Parts(new()
        {
            PastGenerations =
            [
                new()
                {
                    SwarmRequestId = 1001,
                    Outputs =
                    [
                        new() { Image = new() { Data = "QQ==", MediaType = "image/png", Label = "Past Generation 1 Output 1" }, Prompt = "a red cat", Metadata = "{\"seed\":1}" },
                        new() { Image = new() { Data = "Qg==", MediaType = "image/png", Label = "Past Generation 1 Output 2" }, Prompt = "a blue cat", Metadata = "{\"seed\":2}" }
                    ]
                }
            ]
        });

        int first = parts.IndexOf(("text", "Past Generation 1 Output 1\nPrompt: a red cat"));
        int second = parts.IndexOf(("text", "Past Generation 1 Output 2\nPrompt: a blue cat"));
        Xunit.Assert.True(first > 0 && second > first);
        Xunit.Assert.Equal(("image_url", "data:image/png;base64,QQ=="), parts[first + 1]);
        Xunit.Assert.Equal(("text", "Raw SwarmUI metadata for Past Generation 1 Output 1:\n{\"seed\":1}"), parts[first + 2]);
        Xunit.Assert.DoesNotContain(parts, part => part.Value == "CURRENT PROMPT IMAGES");
    }

    [Xunit.Fact]
    public void ActiveModel_CarriesTriggerWeightsAndScope()
    {
        List<(string Type, string Value)> parts = Parts(new()
        {
            ActiveModel = new()
            {
                BaseModel = new() { Name = "base", TriggerPhrase = "base-trigger" },
                Loras = [new() { Name = "adapter", TriggerPhrase = "keep-me", UsageHint = "portrait use", Weight = 0.8, TextEncoderWeight = 0.55, ScopeId = 5, Scope = "Base" }]
            }
        });

        string model = parts.Single(part => part.Value.StartsWith("ACTIVE MODEL CONTEXT", StringComparison.Ordinal)).Value;
        Xunit.Assert.Contains("Trigger phrase: base-trigger", model);
        Xunit.Assert.Contains("Trigger phrase: keep-me", model);
        Xunit.Assert.Contains("Model weight: 0.8", model);
        Xunit.Assert.Contains("Text encoder weight: 0.55", model);
        Xunit.Assert.Contains("Scope: Base (5)", model);
    }

    [Xunit.Fact]
    public void HasImages_CountsPromptImagesAndPastOutputsOnly()
    {
        Xunit.Assert.False(new BackendSchema.PromptContext { ActiveModel = new() }.HasImages);
        Xunit.Assert.True(new BackendSchema.PromptContext { PromptImages = [new() { Data = "QQ==", MediaType = "image/png", Label = "Image 1" }] }.HasImages);
        Xunit.Assert.True(new BackendSchema.PromptContext { PastGenerations = [new() { Outputs = [new() { Image = new() { Data = "QQ==", MediaType = "image/png", Label = "x" }, Prompt = "p", Metadata = "{}" }] }] }.HasImages);
    }
}
