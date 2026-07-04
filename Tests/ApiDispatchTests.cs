using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

/// <summary>Drives the registered handler through SwarmUI's real reflection dispatch (APICall.Call). Routes are registered once by ApiRegistryFixture.</summary>
[Xunit.Collection(ApiRegistryCollectionDefinition.Name)]
public class ApiDispatchTests
{
    [Xunit.Fact]
    public async Task PromptEnhanceRun_DispatchedWithEmptyPrompt_ReturnsClassifiedGenericError()
    {
        SwarmUI.WebAPI.APICall call = SwarmUI.WebAPI.API.APIHandlers["promptenhancerun"];
        JObject input = new() { ["prompt"] = "" };

        JObject result = await call.Call(null!, null!, null!, input);

        Xunit.Assert.False(result["success"]!.Value<bool>());
        Xunit.Assert.Equal("generic", result["errorCategory"]!.Value<string>());
        Xunit.Assert.Contains("No prompt text", result["error"]!.Value<string>());
    }
}
