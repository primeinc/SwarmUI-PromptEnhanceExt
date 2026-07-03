using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

/// <summary>
/// Dispatch-layer gate: drives the registered handler through SwarmUI's real
/// reflection dispatch (the APICall.Call surface built by
/// APICallReflectBuilder, the same path HandleAsyncRequest invokes), so a
/// mismatch between the handler's parameter signature and the generated input
/// mappers surfaces here instead of only in a live host.
/// Routes are registered once by ApiRegistryFixture (the "API registry"
/// collection fixture in ApiRegistryCollection.cs); this class only reads the
/// process-global registry, so it never mutates shared state or depends on run
/// order.
/// </summary>
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
