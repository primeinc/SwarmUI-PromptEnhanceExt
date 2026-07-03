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
    /// runner drives a live SwarmUI BROWSER session end-to-end (render the
    /// Generate tab, click Enhance, observe the prompt mutation). The committed
    /// gates at neighboring boundaries are: `just vendor-ci-test` (boots the
    /// real host with this extension via SwarmUI's own --ci_test mode; any
    /// Logs.Error exits nonzero), this class (real Extension base lifecycle),
    /// ApiRegistrationTests (real API registry + permissions),
    /// BackendTransportTests (real HTTP sockets), and the jsdom suite
    /// (real DOM against the exact served Assets/*.js).
    /// </summary>
    [Xunit.Fact(Skip = "BLOCKED: live browser click-through E2E has no committed runner; the live HOST boot gate is committed as `just vendor-ci-test` — see XML doc.")]
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
