using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

/// <summary>Covers <see cref="PromptEnhance.WebAPI.SessionSettings.ValidateSettings"/> — the server-side guard that
/// rejects out-of-range or wrong-typed settings before they are persisted. Without it a stored <c>timeoutSeconds:0</c>
/// makes every request cancel immediately, a negative timeout throws inside the backend client, and a non-numeric
/// <c>temperature</c> throws when the client reads it. Only keys that are present are validated.</summary>
public class SessionSettingsTests
{
    private static JObject Full() => new()
    {
        ["baseUrl"] = "http://localhost:11434",
        ["timeoutSeconds"] = 60,
        ["temperature"] = 0.7,
        ["maxTokens"] = 1024,
        ["replaceMode"] = "preview"
    };

    [Xunit.Fact]
    public void ValidateSettings_AcceptsAValidObject()
    {
        // Arrange
        JObject input = Full();
        // Act
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        // Assert
        Xunit.Assert.Null(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_IgnoresAbsentKeys()
    {
        // Arrange — a partial save only touches replaceMode; the rest keep their stored/default value.
        JObject input = new() { ["replaceMode"] = "append" };
        // Act
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        // Assert
        Xunit.Assert.Null(error);
    }

    [Xunit.Theory]
    [Xunit.InlineData(0)]
    [Xunit.InlineData(-5)]
    public void ValidateSettings_RejectsNonPositiveTimeout(int timeout)
    {
        // Arrange
        JObject input = Full();
        input["timeoutSeconds"] = timeout;
        // Act
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        // Assert
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsNonNumericTemperature()
    {
        // Arrange — the exact shape that throws Value<double?>() inside the backend client (BackendClient.cs:189).
        JObject input = Full();
        input["temperature"] = "abc";
        // Act
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        // Assert
        AssertRejected(error);
    }

    [Xunit.Theory]
    [Xunit.InlineData(-0.1)]
    [Xunit.InlineData(2.1)]
    public void ValidateSettings_RejectsTemperatureOutOfRange(double temperature)
    {
        // Arrange
        JObject input = Full();
        input["temperature"] = temperature;
        // Act
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        // Assert
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsNonPositiveMaxTokens()
    {
        // Arrange
        JObject input = Full();
        input["maxTokens"] = 0;
        // Act
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        // Assert
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsUnknownReplaceMode()
    {
        // Arrange
        JObject input = Full();
        input["replaceMode"] = "obliterate";
        // Act
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        // Assert
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsEmptyBaseUrl()
    {
        // Arrange
        JObject input = Full();
        input["baseUrl"] = "";
        // Act
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        // Assert
        AssertRejected(error);
    }

    private static void AssertRejected(JObject? error)
    {
        Xunit.Assert.NotNull(error);
        Xunit.Assert.False(error!["success"]!.Value<bool>());
        Xunit.Assert.Equal("generic", error["errorCategory"]!.Value<string>());
    }
}
