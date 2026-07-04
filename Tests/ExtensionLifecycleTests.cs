namespace PromptEnhance.Tests;

/// <summary>Extension-boundary gates against the real SwarmUI assembly.</summary>
public class ExtensionLifecycleTests
{
    [Xunit.Fact(Skip = "BLOCKED: live browser click-through E2E has no committed runner; the live HOST boot gate is `just vendor-ci-test`.")]
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
