namespace PromptEnhance.Tests;

/// <summary>Extension-boundary gates against the real SwarmUI assembly.</summary>
public class ExtensionLifecycleTests
{
    [Xunit.Fact]
    public void OnPreInit_RegistersAssetsAndLicense_ThroughRealExtensionBase()
    {
        PromptEnhanceExtension extension = new();

        extension.OnPreInit();

        Xunit.Assert.Equal("MIT", extension.License);
        Xunit.Assert.Equal(["Assets/contracts.js", "Assets/settings.js", "Assets/promptenhance.js"], extension.ScriptFiles);
        Xunit.Assert.Equal(["Assets/promptenhance.css"], extension.StyleSheetFiles);
    }
}
