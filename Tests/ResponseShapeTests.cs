using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

public class ResponseShapeTests
{
    [Xunit.Fact]
    public void CreateModelsResponse_EmitsLowercaseIdAndName()
    {
        List<WebAPI.Models.ModelData> models = [new() { Id = "mock-enhancer", Name = "Mock Enhancer" }];

        JObject response = WebAPI.PromptEnhanceAPI.CreateModelsResponse(models);

        Xunit.Assert.True(response["success"]!.Value<bool>());
        JArray arr = (JArray)response["models"]!;
        Xunit.Assert.Single(arr);
        JObject first = (JObject)arr[0];
        Xunit.Assert.Equal("mock-enhancer", first["id"]!.Value<string>());
        Xunit.Assert.Equal("Mock Enhancer", first["name"]!.Value<string>());
        Xunit.Assert.Null(first["Id"]);
        Xunit.Assert.Null(first["Name"]);
    }

    [Xunit.Fact]
    public void CreateModelsResponse_HandlesNullList()
    {
        JObject response = WebAPI.PromptEnhanceAPI.CreateModelsResponse(null!);

        Xunit.Assert.True(response["success"]!.Value<bool>());
        Xunit.Assert.Empty((JArray)response["models"]!);
    }
}
