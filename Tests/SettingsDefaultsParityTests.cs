using System.IO;
using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

public class SettingsDefaultsParityTests
{
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
        string serverDefault = WebAPI.SessionSettings.Defaults.Value<string>("systemPrompt")!;
        string js = File.ReadAllText(LocateSettingsJs());

        Xunit.Assert.False(string.IsNullOrEmpty(serverDefault));
        Xunit.Assert.Contains(serverDefault, js);
    }
}
