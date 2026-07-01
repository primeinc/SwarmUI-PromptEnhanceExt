using System.IO;
using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

/// <summary>Guards Finding F8: the client-side default in <c>Assets/settings.js</c> for <c>systemPrompt</c> must equal
/// the server-side <see cref="PromptEnhance.WebAPI.SessionSettings.Defaults"/> value, so a backend-down init can never
/// persist an empty system prompt over the real default.</summary>
public class SettingsDefaultsParityTests
{
    /// <summary>Walks up from the test output directory to find the extension's <c>Assets/settings.js</c>.</summary>
    private static string LocateSettingsJs()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "settings.js");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate Assets/settings.js from the test output directory.");
    }

    [Xunit.Fact]
    public void ClientSystemPromptDefault_MatchesServerDefaultVerbatim()
    {
        // Arrange
        string serverDefault = WebAPI.SessionSettings.Defaults.Value<string>("systemPrompt")!;
        string js = File.ReadAllText(LocateSettingsJs());

        // Assert
        Xunit.Assert.False(string.IsNullOrEmpty(serverDefault)); // source-of-truth default is a real instruction, not ''
        Xunit.Assert.Contains(serverDefault, js);                // the JS default literal carries the exact server text
    }
}
