using PromptEnhance.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace PromptEnhance;

public class PromptEnhanceExtension : Extension
{
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

    public override void OnInit()
    {
        PromptEnhanceAPI.Register();
    }
}
