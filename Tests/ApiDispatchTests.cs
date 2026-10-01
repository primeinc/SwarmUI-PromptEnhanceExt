using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

/// <summary>Drives the registered handler through SwarmUI's real reflection dispatch (APICall.Call). Routes are registered once by ApiRegistryFixture.</summary>
[Xunit.Collection(ApiRegistryCollectionDefinition.Name)]
public class ApiDispatchTests
{
    [Xunit.Theory]
    [Xunit.InlineData("""{}""")]
    [Xunit.InlineData("""{"prompt":""}""")]
    [Xunit.InlineData("""{"prompt":null}""")]
    [Xunit.InlineData("""{"prompt":{"a":1}}""")]
    [Xunit.InlineData("""{"prompt":["x"]}""")]
    [Xunit.InlineData("""{"prompt":5}""")]
    public async Task PromptEnhanceRun_DispatchedWithoutAStringPrompt_ReturnsInvalidRequest(string json)
    {
        SwarmUI.WebAPI.APICall call = SwarmUI.WebAPI.API.APIHandlers["promptenhancerun"];
        JObject input = JObject.Parse(json);

        JObject result = await call.Call(null!, null!, null!, input);

        Xunit.Assert.False(result["success"]!.Value<bool>());
        Xunit.Assert.Equal("invalid_request", result["error_id"]!.Value<string>());
        Xunit.Assert.Contains("No prompt text", result["error"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task PromptEnhanceRun_DispatchedWithLegacyMediaAndSwarmInput_ReturnsInvalidRequest()
    {
        SwarmUI.WebAPI.APICall call = SwarmUI.WebAPI.API.APIHandlers["promptenhancerun"];
        JObject input = JObject.Parse("""{"prompt":"a cat","swarmInput":{},"media":[{"type":"base64","data":"QUJD","mediaType":"image/png"}]}""");

        JObject result = await call.Call(null!, null!, null!, input);

        Xunit.Assert.Equal("invalid_request", result["error_id"]!.Value<string>());
        Xunit.Assert.Contains("only supported", result["error"]!.Value<string>());
    }
}
