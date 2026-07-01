namespace PromptEnhance.Tests;

/// <summary>Integration gate at the real SwarmUI extension boundary: constructs <see cref="PromptEnhanceExtension"/>
/// and runs its <c>OnPreInit</c> lifecycle hook against the real SwarmUI <see cref="SwarmUI.Core.Extension"/> base,
/// proving the entrypoint registers its Generate-tab assets and license through the real extension surface — not a mock.
/// The complementary boundary (OnInit → API route registration into SwarmUI's real <c>API.APIHandlers</c>) is covered by
/// <see cref="ApiRegistrationTests"/>; the two together are the committed equivalent of a live load for the
/// registration boundary. A full browser-driven Generate-tab run remains BLOCKED (see <c>docs/AUDIT.md</c> §6, §7).</summary>
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
