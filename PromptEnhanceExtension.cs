using PromptEnhance.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace PromptEnhance;

/// <summary>Extension entrypoint: asset registration in <see cref="OnPreInit"/>; API routes, the API key, and the generation history in <see cref="OnInit"/>.</summary>
public class PromptEnhanceExtension : Extension
{
    /// <summary>Registers the frontend assets in dependency order: contracts.js, settings.js, swarminput.js, then promptenhance.js. The committed Assets/*.js files are tsc build output of Frontend/*.ts; never hand-edit them.</summary>
    public override void OnPreInit()
    {
        Description = "Enhance the current Generate-tab prompt through a configurable OpenAI-compatible chat endpoint.";
        License = "MIT";
        ScriptFiles.Add("Assets/contracts.js");
        ScriptFiles.Add("Assets/settings.js");
        ScriptFiles.Add("Assets/swarminput.js");
        ScriptFiles.Add("Assets/promptenhance.js");
        StyleSheetFiles.Add("Assets/promptenhance.css");
        Logs.Init("PromptEnhance extension loaded.");
    }

    /// <summary>Registers the five API routes with their permissions and the backend API key in SwarmUI's User → API Keys table, then opens the generation history and subscribes it to SwarmUI's generation events.</summary>
    public override void OnInit()
    {
        PromptEnhanceAPI.Register();
        UpstreamApiKey.Register();
        GenerationHistory.Open(GenerationHistory.DefaultPath);
        GenerationHistory.Attach();
    }

    /// <summary>Unsubscribes and closes the generation history.</summary>
    public override void OnShutdown()
    {
        GenerationHistory.Detach();
        GenerationHistory.Close();
    }
}
