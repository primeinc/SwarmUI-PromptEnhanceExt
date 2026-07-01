using PromptEnhance.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace PromptEnhance;

/// <summary>PromptEnhance — a minimal Generate-tab prompt enhancer backed by a configurable OpenAI-compatible endpoint.
/// The extension registers its web assets (a button + a settings panel) and its API routes; all behavior lives in the
/// registered JS and the WebAPI classes. See README.md for the product contract and the one external connection made.</summary>
public class PromptEnhanceExtension : Extension
{
    /// <summary>Register web assets and metadata before the program loads.</summary>
    public override void OnPreInit()
    {
        Description = "Enhance the current Generate-tab prompt through a configurable OpenAI-compatible chat endpoint.";
        License = "MIT";
        ScriptFiles.Add("Assets/promptenhance.js");
        ScriptFiles.Add("Assets/settings.js");
        StyleSheetFiles.Add("Assets/promptenhance.css");
        StyleSheetFiles.Add("Assets/settings.css");
        Logs.Init("PromptEnhance extension loaded.");
    }

    /// <summary>Register the API routes once program features are prepped.</summary>
    public override void OnInit()
    {
        PromptEnhanceAPI.Register();
    }
}
