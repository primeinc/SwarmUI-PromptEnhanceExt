using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

public class BackendClientTests
{
    [Xunit.Theory]
    [Xunit.InlineData("http://localhost:11434", "http://localhost:11434")]
    [Xunit.InlineData("http://localhost:11434/", "http://localhost:11434")]
    [Xunit.InlineData("http://localhost:11434/v1", "http://localhost:11434")]
    [Xunit.InlineData("http://localhost:11434/v1/", "http://localhost:11434")]
    [Xunit.InlineData("https://api.example.com/v1", "https://api.example.com")]
    [Xunit.InlineData("HTTP://Localhost:1234/V1", "HTTP://Localhost:1234")]
    public void NormalizeBaseUrl_StripsTrailingSlashAndV1(string input, string expected)
    {
        string? result = WebAPI.BackendClient.NormalizeBaseUrl(input);

        Xunit.Assert.Equal(expected, result);
    }

    [Xunit.Theory]
    [Xunit.InlineData(null)]
    [Xunit.InlineData("")]
    [Xunit.InlineData("   ")]
    [Xunit.InlineData("not a url")]
    [Xunit.InlineData("ftp://example.com")]
    [Xunit.InlineData("/relative/path")]
    [Xunit.InlineData("http://169.254.169.254/latest/meta-data?x=")]
    [Xunit.InlineData("http://internal/any/path#")]
    [Xunit.InlineData("http://internal/any/path#frag")]
    [Xunit.InlineData("http://user:pass@localhost:11434")]
    [Xunit.InlineData("http://localhost:11434/v1?")]
    public void NormalizeBaseUrl_ReturnsNullForInvalid(string? input)
    {
        string? result = WebAPI.BackendClient.NormalizeBaseUrl(input!);

        Xunit.Assert.Null(result);
    }

    [Xunit.Theory]
    [Xunit.InlineData("{}")]
    [Xunit.InlineData("{\"prompt\":\"\"}")]
    [Xunit.InlineData("{\"prompt\":\"   \"}")]
    public async Task PromptEnhanceRun_EmptyPrompt_IsInvalidRequestBeforeAnySessionUse(string rawJson)
    {
        JObject rawInput = JObject.Parse(rawJson);

        JObject result = await WebAPI.BackendClient.PromptEnhanceRun(null!, rawInput["prompt"]?.Value<string>()!, rawInput);

        Xunit.Assert.False(result["success"]!.Value<bool>());
        Xunit.Assert.Equal("invalid_request", result["error_id"]!.Value<string>());
        Xunit.Assert.Contains("No prompt text", result["error"]!.Value<string>());
    }

    [Xunit.Theory]
    [Xunit.InlineData("{\"prompt\":\"a cat\",\"media\":[]}", "'media'")]
    [Xunit.InlineData("{\"prompt\":\"a cat\",\"context\":{}}", "'context'")]
    [Xunit.InlineData("{\"prompt\":\"a cat\",\"swarmInput\":[]}", "must be an object")]
    [Xunit.InlineData("{\"prompt\":\"a cat\",\"swarmInput\":\"model\"}", "must be an object")]
    public async Task PromptEnhanceRun_MalformedBody_IsInvalidRequestBeforeAnySessionUse(string rawJson, string expected)
    {
        JObject rawInput = JObject.Parse(rawJson);

        JObject result = await WebAPI.BackendClient.PromptEnhanceRun(null!, "a cat", rawInput);

        Xunit.Assert.Equal("invalid_request", result["error_id"]!.Value<string>());
        Xunit.Assert.Contains(expected, result["error"]!.Value<string>());
    }
}
