using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

/// <summary>Covers the wire shape of <see cref="PromptEnhance.WebAPI.PromptEnhanceAPI.CreateModelsResponse"/> — the
/// model list <c>settings.js</c> renders into the dropdown. The frontend reads <c>m.id</c> / <c>m.name</c> (lowercase);
/// a PascalCase payload (which <c>JArray.FromObject(models)</c> emits for <c>ModelData</c>, since Newtonsoft ignores the
/// System.Text.Json attributes) leaves the dropdown silently empty. This regression was caught by a live SwarmUI run;
/// the test pins the lowercase contract so it cannot recur.</summary>
public class ResponseShapeTests
{
    [Xunit.Fact]
    public void CreateModelsResponse_EmitsLowercaseIdAndName()
    {
        // Arrange
        List<WebAPI.Models.ModelData> models = [new() { Id = "mock-enhancer", Name = "Mock Enhancer" }];

        // Act
        JObject response = WebAPI.PromptEnhanceAPI.CreateModelsResponse(models);

        // Assert
        Xunit.Assert.True(response["success"]!.Value<bool>());
        JArray arr = (JArray)response["models"]!;
        Xunit.Assert.Single(arr);
        JObject first = (JObject)arr[0];
        Xunit.Assert.Equal("mock-enhancer", first["id"]!.Value<string>());
        Xunit.Assert.Equal("Mock Enhancer", first["name"]!.Value<string>());
        // Guard against the PascalCase regression the live UI surfaced.
        Xunit.Assert.Null(first["Id"]);
        Xunit.Assert.Null(first["Name"]);
    }

    [Xunit.Fact]
    public void CreateModelsResponse_HandlesNullList()
    {
        // Act
        JObject response = WebAPI.PromptEnhanceAPI.CreateModelsResponse(null!);

        // Assert
        Xunit.Assert.True(response["success"]!.Value<bool>());
        Xunit.Assert.Empty((JArray)response["models"]!);
    }
}
