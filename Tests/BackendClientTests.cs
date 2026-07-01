namespace PromptEnhance.Tests;

/// <summary>Covers <see cref="PromptEnhance.WebAPI.BackendClient.NormalizeBaseUrl"/> — the rule that lets a user type
/// either a server root or a <c>/v1</c> URL and still have the owned seams resolve to <c>{base}/v1/models</c> and
/// <c>{base}/v1/chat/completions</c>. Wrong normalization silently points requests at the wrong path.</summary>
public class BackendClientTests
{
    [Xunit.Theory]
    [Xunit.InlineData("http://localhost:11434", "http://localhost:11434")]
    [Xunit.InlineData("http://localhost:11434/", "http://localhost:11434")]
    [Xunit.InlineData("http://localhost:11434/v1", "http://localhost:11434")]
    [Xunit.InlineData("http://localhost:11434/v1/", "http://localhost:11434")]
    [Xunit.InlineData("https://api.example.com/v1", "https://api.example.com")]
    [Xunit.InlineData("HTTP://Localhost:1234/V1", "HTTP://Localhost:1234")] // /v1 stripped case-insensitively, casing preserved
    public void NormalizeBaseUrl_StripsTrailingSlashAndV1(string input, string expected)
    {
        // Act
        string? result = WebAPI.BackendClient.NormalizeBaseUrl(input);

        // Assert
        Xunit.Assert.Equal(expected, result);
    }

    [Xunit.Theory]
    [Xunit.InlineData(null)]
    [Xunit.InlineData("")]
    [Xunit.InlineData("   ")]
    [Xunit.InlineData("not a url")]
    [Xunit.InlineData("ftp://example.com")] // only http(s) accepted
    [Xunit.InlineData("/relative/path")]
    public void NormalizeBaseUrl_ReturnsNullForInvalid(string? input)
    {
        // Act
        string? result = WebAPI.BackendClient.NormalizeBaseUrl(input!);

        // Assert
        Xunit.Assert.Null(result);
    }
}
