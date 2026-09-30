using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Text2Image;

namespace PromptEnhance.Tests;

/// <summary>The parts of SwarmUI startup that the context and history code reads: the registered T2I parameter types (Program.cs:314) and the Stable-Diffusion and LoRA model registries, holding the test models below.
/// These are process-global SwarmUI statics that are set once and never restored; tests that use them rely on Tests/AssemblyInfo.cs disabling test parallelization.</summary>
internal static class SwarmHost
{
    /// <summary>A PNG of one gray level, base64.</summary>
    public static string Png(byte gray, int width = 1, int height = 1)
    {
        using SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image = new(width, height, new SixLabors.ImageSharp.PixelFormats.Rgba32(gray, gray, gray, 255));
        using MemoryStream stream = new();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    /// <summary>A black 1x1 PNG, base64.</summary>
    public static readonly string PngBase64 = Png(0);

    /// <summary>A white 1x1 PNG, base64.</summary>
    public static readonly string PngBase64B = Png(255);

    private static readonly object InitLock = new();

    private static bool Initialized;

    private static T2IModel Model(T2IModelHandler handler, string name, string title, string trigger)
    {
        return new T2IModel(handler, "", name, name)
        {
            Description = $"{title} description",
            ModelClass = new T2IModelClass { ID = "sdxl-base", Name = "SDXL Base", CompatClass = new T2IModelCompatClass { ID = "sdxl" } },
            // A cached hash, as a scanned model has: SwarmUI returns it instead of hashing the (absent) file (T2IModel.cs:78-81).
            Metadata = new T2IModelHandler.ModelMetadataStore { Title = title, TriggerPhrase = trigger, UsageHint = $"{title} usage", Tags = ["tag-a", "tag-b"], Hash = $"0x{new string('0', 64)}" }
        };
    }

    /// <summary>Registers the parameter types and model registries once per test process.</summary>
    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (Initialized)
            {
                return;
            }
            if (T2IParamTypes.Types.Count == 0)
            {
                T2IParamTypes.RegisterDefaults();
            }
            T2IModelHandler main = new() { ModelType = "Stable-Diffusion" };
            main.Models["base-model.safetensors"] = Model(main, "base-model.safetensors", "Base Title", "base-trigger");
            main.Models["restricted/hidden.safetensors"] = Model(main, "restricted/hidden.safetensors", "Hidden", "hidden-trigger");
            T2IModelHandler loras = new() { ModelType = "LoRA" };
            loras.Models["lora-a.safetensors"] = Model(loras, "lora-a.safetensors", "Lora A", "trigger-a");
            loras.Models["lora-b.safetensors"] = Model(loras, "lora-b.safetensors", "Lora B", "trigger-b");
            Program.T2IModelSets["Stable-Diffusion"] = main;
            Program.T2IModelSets["LoRA"] = loras;
            // SwarmUI's only built-in extra-model provider reads Program.Backends, which this host does not start; its answer with no remote Swarm backends running is empty (ModelsAPI.cs:47-54).
            SwarmUI.WebAPI.ModelsAPI.ExtraModelProviders["remote_swarm"] = _ => [];
            Initialized = true;
        }
    }

    /// <summary>A real session whose role grants every permission, with optional model-blacklist prefixes.</summary>
    public static Session PermittedSession(params string[] modelBlacklist)
    {
        EnsureInitialized();
        Session session = TestSessions.MakeRealSession();
        Role role = new("promptenhance_test");
        role.Data.PermissionFlags.Add("*");
        role.Data.ModelBlacklist.UnionWith(modelBlacklist);
        session.User.CalculatedRole = role;
        return session;
    }

    /// <summary>Saves PromptEnhance settings for the session's user.</summary>
    public static void SaveSettings(Session session, string json)
    {
        session.User.SaveGenericData("promptenhance", "config", json);
    }
}
