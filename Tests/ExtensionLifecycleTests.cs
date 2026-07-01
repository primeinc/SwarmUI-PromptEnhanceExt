namespace PromptEnhance.Tests;

/// <summary>
/// Extension-boundary gates: these run against the REAL SwarmUI assembly
/// (the test project references the host's SwarmUI.csproj), so registration
/// happens through the same Extension base and API registry a live host uses.
/// </summary>
public class ExtensionLifecycleTests
{
    /// <summary>
    /// Explicit BLOCKED marker, kept visible in every test run: no committed
    /// runner drives a live SwarmUI browser session end-to-end (launch host,
    /// render Generate tab, click Enhance, observe mutation). The committed
    /// equivalents at the same boundary are: this class (real Extension base
    /// lifecycle), ApiRegistrationTests (real API registry + permissions),
    /// BackendTransportTests (real HTTP sockets), and the jsdom suite
    /// (real DOM against the exact served Assets/*.js).
    /// </summary>
    [Xunit.Fact(Skip = "BLOCKED: live SwarmUI browser E2E has no committed runner; see XML doc for the committed equivalent gates.")]
    public void LiveSwarmUIBrowserE2E_IsExplicitlyBlocked()
    {
    }

    [Xunit.Fact]
    public void OnPreInit_RegistersAssetsAndLicense_ThroughRealExtensionBase()
    {
        PromptEnhanceExtension extension = new();

        extension.OnPreInit();

        Xunit.Assert.Equal("MIT", extension.License);
        Xunit.Assert.Equal("Assets/contracts.js", extension.ScriptFiles[0]);
        Xunit.Assert.Contains("Assets/promptenhance.js", extension.ScriptFiles);
        Xunit.Assert.Contains("Assets/settings.js", extension.ScriptFiles);
        Xunit.Assert.Contains("Assets/promptenhance.css", extension.StyleSheetFiles);
        Xunit.Assert.Contains("Assets/settings.css", extension.StyleSheetFiles);
    }
}
