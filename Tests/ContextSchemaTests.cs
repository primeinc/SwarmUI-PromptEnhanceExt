using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

public class ContextSchemaTests
{
    private static JsonElement Build(BackendSchema.PromptContext context)
    {
        object body = BackendSchema.BuildChatRequest("m", "sys", "CURRENT", [], 0.7, 1024, context);
        return JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement;
    }

    [Xunit.Fact]
    public void PromptImages_PreserveOrderAndExplicitIdentity()
    {
        BackendSchema.PromptContext context = new()
        {
            PromptImages =
            [
                new() { Type = "base64", Data = "QQ==", MediaType = "image/png", Label = "Image 1" },
                new() { Type = "base64", Data = "Qg==", MediaType = "image/jpeg", Label = "Image 2" }
            ]
        };

        JsonElement parts = Build(context).GetProperty("messages")[1].GetProperty("content");
        List<string> text = [];
        List<string> urls = [];
        foreach (JsonElement part in parts.EnumerateArray())
        {
            if (part.GetProperty("type").GetString() == "text")
            {
                text.Add(part.GetProperty("text").GetString()!);
            }
            else if (part.GetProperty("type").GetString() == "image_url")
            {
                urls.Add(part.GetProperty("image_url").GetProperty("url").GetString()!);
            }
        }

        Xunit.Assert.Contains("Image 1", text);
        Xunit.Assert.Contains("Image 2", text);
        Xunit.Assert.Equal(2, urls.Count);
        Xunit.Assert.Contains("CURRENT PROMPT TO ENHANCE:\nCURRENT", text);
        Xunit.Assert.StartsWith("data:image/png;base64,QQ==", urls[0]);
        Xunit.Assert.StartsWith("data:image/jpeg;base64,Qg==", urls[1]);
    }

    [Xunit.Fact]
    public void PastGeneration_IsSeparateFromCurrentImages_AndCarriesRawMetadata()
    {
        const string metadata = "{\"sui_image_params\":{\"seed\":42}}";
        BackendSchema.PromptContext context = new()
        {
            PromptImages = [new() { Type = "base64", Data = "QQ==", MediaType = "image/png", Label = "Image 1" }],
            PastGenerations =
            [
                new()
                {
                    RequestId = "123",
                    Prompt = "OLD PROMPT",
                    Outputs =
                    [
                        new()
                        {
                            Image = new() { Type = "base64", Data = "Qg==", MediaType = "image/png", Label = "Past Generation 1 Output 1" },
                            Metadata = metadata
                        }
                    ]
                }
            ]
        };

        string json = JsonSerializer.Serialize(BackendSchema.BuildChatRequest("m", "sys", "CURRENT", [], 0.7, 1024, context));

        Xunit.Assert.Contains("CURRENT PROMPT IMAGES", json);
        Xunit.Assert.Contains("PAST GENERATIONS", json);
        Xunit.Assert.Contains("OLD PROMPT", json);
        Xunit.Assert.Contains("Past Generation 1 Output 1", json);
        Xunit.Assert.Contains("seed", json);
        Xunit.Assert.Contains("42", json);
    }

    [Xunit.Fact]
    public void ActiveLoraContext_CarriesTriggerWeightsAndScope()
    {
        BackendSchema.PromptContext context = new()
        {
            ActiveModel = new()
            {
                BaseModel = new() { Name = "base", TriggerPhrase = "base-trigger" },
                Loras =
                [
                    new()
                    {
                        Name = "adapter",
                        TriggerPhrase = "keep-me",
                        UsageHint = "portrait use",
                        Weight = 0.8,
                        TextEncoderWeight = 0.55,
                        ScopeId = 5,
                        Scope = "Base"
                    }
                ]
            }
        };

        string json = JsonSerializer.Serialize(BackendSchema.BuildChatRequest("m", "sys", "CURRENT", [], 0.7, 1024, context));

        Xunit.Assert.Contains("keep-me", json);
        Xunit.Assert.Contains("0.8", json);
        Xunit.Assert.Contains("0.55", json);
        Xunit.Assert.Contains("Base", json);
        Xunit.Assert.Contains("Preserve required trigger phrases", json);
    }

    [Xunit.Fact]
    public void ParseContext_PreservesMetadataAndEffectiveLoraValues()
    {
        JObject raw = JObject.Parse("""
        {
          "promptImages": [{"type":"base64","data":"QQ==","mediaType":"image/png","label":"Image 1"}],
          "pastGenerations": [{
            "requestId":"9",
            "prompt":"old",
            "outputs":[{"image":{"type":"base64","data":"Qg==","mediaType":"image/png","label":"Past Generation 1 Output 1"},"metadata":"RAW"}]
          }],
          "activeModel": {
            "baseModel":{"name":"base","triggerPhrase":"base-token","tags":["photo"]},
            "loras":[{"name":"lora","weight":0.75,"textEncoderWeight":0.25,"scopeId":0,"scope":"Global","triggerPhrase":"lora-token"}]
          }
        }
        """);

        BackendSchema.PromptContext context = WebAPI.BackendClient.ParseContext(raw);

        Xunit.Assert.Equal("Image 1", context.PromptImages[0].Label);
        Xunit.Assert.Equal("RAW", context.PastGenerations[0].Outputs[0].Metadata);
        Xunit.Assert.Equal(0.75, context.ActiveModel.Loras[0].Weight);
        Xunit.Assert.Equal(0.25, context.ActiveModel.Loras[0].TextEncoderWeight);
        Xunit.Assert.Equal("Global", context.ActiveModel.Loras[0].Scope);
        Xunit.Assert.Equal("base-token", context.ActiveModel.BaseModel.TriggerPhrase);
    }

    [Xunit.Theory]
    [Xunit.InlineData("[]")]
    [Xunit.InlineData("{\"promptImages\":{}}")]
    [Xunit.InlineData("{\"promptImages\":[null]}")]
    [Xunit.InlineData("{\"pastGenerations\":{}}")]
    [Xunit.InlineData("{\"pastGenerations\":[null]}")]
    [Xunit.InlineData("{\"pastGenerations\":[{\"requestId\":\"9\",\"prompt\":\"x\",\"outputs\":{}}]}")]
    [Xunit.InlineData("{\"pastGenerations\":[{\"requestId\":\"9\",\"prompt\":\"x\",\"outputs\":[null]}]}")]
    [Xunit.InlineData("{\"activeModel\":[]}")]
    [Xunit.InlineData("{\"activeModel\":{\"loras\":{}}}")]
    [Xunit.InlineData("{\"activeModel\":{\"loras\":[null]}}")]
    [Xunit.InlineData("{\"activeModel\":{\"loras\":[{\"name\":\"l\",\"weight\":\"bad\",\"textEncoderWeight\":1,\"scopeId\":0,\"scope\":\"Global\"}]}}")]
    public void ParseContext_RejectsMalformedContainersAndEntries(string json)
    {
        JToken raw = JToken.Parse(json);

        Xunit.Assert.Throws<ArgumentException>(() => WebAPI.BackendClient.ParseContext(raw));
    }

    [Xunit.Fact]
    public async Task PromptEnhanceRun_MalformedContext_IsClassifiedBeforeSessionUse()
    {
        JObject raw = JObject.Parse("""
        {"prompt":"cat","context":{"promptImages":{}}}
        """);

        JObject result = await WebAPI.BackendClient.PromptEnhanceRun(raw, null!);

        Xunit.Assert.False(result["success"]!.Value<bool>());
        Xunit.Assert.Equal("unsupported_image", result["error_id"]!.Value<string>());
        Xunit.Assert.Contains("promptImages", result["error"]!.Value<string>());
    }

    [Xunit.Fact]
    public void ParseContext_RejectsDatalessHistoricalImage()
    {
        JObject raw = JObject.Parse("""
        {"pastGenerations":[{"requestId":"9","prompt":"old","outputs":[{"image":{"type":"base64","mediaType":"image/png"},"metadata":"RAW"}]}]}
        """);

        Xunit.Assert.Throws<ArgumentException>(() => WebAPI.BackendClient.ParseContext(raw));
    }
}
