using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

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
        JObject input = Full();
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        Xunit.Assert.Null(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_IgnoresAbsentKeys()
    {
        JObject input = new() { ["replaceMode"] = "append" };
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        Xunit.Assert.Null(error);
    }

    [Xunit.Theory]
    [Xunit.InlineData(0)]
    [Xunit.InlineData(-5)]
    public void ValidateSettings_RejectsNonPositiveTimeout(int timeout)
    {
        JObject input = Full();
        input["timeoutSeconds"] = timeout;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsNonNumericTemperature()
    {
        JObject input = Full();
        input["temperature"] = "abc";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Theory]
    [Xunit.InlineData(-0.1)]
    [Xunit.InlineData(2.1)]
    public void ValidateSettings_RejectsTemperatureOutOfRange(double temperature)
    {
        JObject input = Full();
        input["temperature"] = temperature;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsNonPositiveMaxTokens()
    {
        JObject input = Full();
        input["maxTokens"] = 0;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsTimeoutAboveInt32()
    {
        JObject input = Full();
        input["timeoutSeconds"] = (long)int.MaxValue + 1;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsMaxTokensAboveInt32()
    {
        JObject input = Full();
        input["maxTokens"] = (long)int.MaxValue + 1;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsUnknownReplaceMode()
    {
        JObject input = Full();
        input["replaceMode"] = "obliterate";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsEmptyBaseUrl()
    {
        JObject input = Full();
        input["baseUrl"] = "";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    private static void AssertRejected(JObject? error)
    {
        Xunit.Assert.NotNull(error);
        Xunit.Assert.False(error!["success"]!.Value<bool>());
        Xunit.Assert.Equal("generic", error["errorCategory"]!.Value<string>());
    }
}
