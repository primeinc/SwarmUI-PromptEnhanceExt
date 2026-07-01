namespace PromptEnhance.Tests;

public class ExtensionLifecycleTests
{
    [Xunit.Fact]
    public void OnPreInit_RegistersAssetsAndLicense_ThroughRealExtensionBase()
    {
        PromptEnhanceExtension extension = new();

        extension.OnPreInit();

        Xunit.Assert.Equal("MIT", extension.License);
        Xunit.Assert.Contains("Assets/promptenhance.js", extension.ScriptFiles);
        Xunit.Assert.Contains("Assets/settings.js", extension.ScriptFiles);
        Xunit.Assert.Contains("Assets/promptenhance.css", extension.StyleSheetFiles);
        Xunit.Assert.Contains("Assets/settings.css", extension.StyleSheetFiles);
    }
}
