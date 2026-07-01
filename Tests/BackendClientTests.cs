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
    public void NormalizeBaseUrl_ReturnsNullForInvalid(string? input)
    {
        string? result = WebAPI.BackendClient.NormalizeBaseUrl(input!);

        Xunit.Assert.Null(result);
    }
}
