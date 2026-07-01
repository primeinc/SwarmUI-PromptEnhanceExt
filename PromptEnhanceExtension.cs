using PromptEnhance.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace PromptEnhance;

/// <summary>
/// Extension entrypoint. SwarmUI discovers this class by convention
/// (class name matches the file name, derives from <see cref="Extension"/>)
/// and drives it through the host lifecycle: asset registration happens in
/// <see cref="OnPreInit"/>, API route registration in <see cref="OnInit"/>.
/// </summary>
public class PromptEnhanceExtension : Extension
{
    /// <summary>
    /// Registers the frontend assets. Order matters: contracts.js carries the
    /// shared boundary adapters and must load before the two feature scripts.
    /// The committed Assets/*.js files are tsc build output of Frontend/*.ts
    /// (the authoritative source); never hand-edit them.
    /// </summary>
    public override void OnPreInit()
    {
        Description = "Enhance the current Generate-tab prompt through a configurable OpenAI-compatible chat endpoint.";
        License = "MIT";
        ScriptFiles.Add("Assets/contracts.js");
        ScriptFiles.Add("Assets/promptenhance.js");
        ScriptFiles.Add("Assets/settings.js");
        StyleSheetFiles.Add("Assets/promptenhance.css");
        StyleSheetFiles.Add("Assets/settings.css");
        Logs.Init("PromptEnhance extension loaded.");
    }

    /// <summary>Registers the five API routes (list models, run, get/save/reset settings) with their permissions.</summary>
    public override void OnInit()
    {
        PromptEnhanceAPI.Register();
    }
}
