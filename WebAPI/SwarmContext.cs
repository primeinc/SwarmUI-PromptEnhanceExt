using System.Globalization;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace PromptEnhance.WebAPI;

/// <summary>Resolves the SwarmUI generation input the browser sent into Prompt Images and the active model stack, through SwarmUI's own parameter parser (<see cref="T2IAPI.RequestToParams"/>) and model registry.</summary>
public static class SwarmContext
{
    /// <summary>The generation-input keys that carry Prompt Images.</summary>
    public static readonly string[] PromptImageKeys = ["promptimages"];

    /// <summary>The generation-input keys that carry the active model stack.</summary>
    public static readonly string[] ActiveModelKeys = ["model", "loras", "loraweights", "loratencweights", "lorasectionconfinement"];

    /// <summary>Display names for SwarmUI's LoRA section-confinement ids (<see cref="T2IParamInput"/> SectionID_* fields; 0 is global).</summary>
    private static string ScopeName(int id)
    {
        if (id == 0)
        {
            return "Global";
        }
        if (id == T2IParamInput.SectionID_BaseOnly)
        {
            return "Base";
        }
        if (id == T2IParamInput.SectionID_Refiner)
        {
            return "Refiner";
        }
        if (id == T2IParamInput.SectionID_Video)
        {
            return "Video";
        }
        if (id == T2IParamInput.SectionID_VideoSwap)
        {
            return "VideoSwap";
        }
        if (id == T2IParamInput.SectionID_PixelDecoder)
        {
            return "PixelDecoder";
        }
        if (id == T2IParamInput.SectionID_SeedVR)
        {
            return "SeedVR";
        }
        return $"Section {id}";
    }

    /// <summary>The per-LoRA list parameters, which may not hold more entries than there are LoRAs: SwarmUI would drop the extras (T2IParamInput.cs:64-86).</summary>
    private static readonly (T2IRegisteredParam<List<string>> Param, string Key)[] LoraLists =
    [
        (T2IParamTypes.LoraWeights, "loraweights"),
        (T2IParamTypes.LoraTencWeights, "loratencweights"),
        (T2IParamTypes.LoraSectionConfinement, "lorasectionconfinement")
    ];

    /// <summary>Builds the Prompt Images and active-model parts of <paramref name="context"/> from <paramref name="swarmInput"/>.
    /// Every key must belong to a channel the user enabled; anything else, anything SwarmUI's parser would silently drop, and every value SwarmUI's parser rejects, throws <see cref="ArgumentException"/>.
    /// Parsing goes through <see cref="T2IAPI.RequestToParams"/>, which builds a <see cref="T2IParamInput"/> for the session: like any SwarmUI request, that takes the user's next request id.</summary>
    public static void Resolve(Session session, JObject swarmInput, bool sendPromptImages, bool sendActiveModelContext, BackendSchema.PromptContext context)
    {
        HashSet<string> allowed = [];
        if (sendPromptImages)
        {
            allowed.UnionWith(PromptImageKeys);
        }
        if (sendActiveModelContext)
        {
            allowed.UnionWith(ActiveModelKeys);
        }
        foreach (JProperty property in swarmInput.Properties())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new ArgumentException($"swarmInput.{property.Name} is not accepted: it is not a key of an enabled context channel.");
            }
        }
        // SwarmUI joins the array and splits it dropping empty entries (T2IAPI.cs:216, T2IParamTypes.cs:1145), so an empty entry must be rejected before parsing.
        if (swarmInput.TryGetValue("promptimages", out JToken rawImages) && (rawImages is not JArray imageEntries || imageEntries.Any(entry => entry.Type != JTokenType.String || string.IsNullOrWhiteSpace(entry.Value<string>()))))
        {
            throw new ArgumentException("swarmInput.promptimages must be an array of non-empty image data strings.");
        }
        // SwarmUI's parser reports a malformed value as a plain Exception naming the parameter (T2IParamTypes.cs:1231-1234); every parse failure here is a malformed request.
        T2IParamInput input;
        try
        {
            input = T2IAPI.RequestToParams(session, swarmInput, applyPresets: false);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(ex.Message, ex);
        }
        int loraCount = input.TryGet(T2IParamTypes.Loras, out List<string> loras) ? loras.Count : 0;
        foreach ((T2IRegisteredParam<List<string>> param, string key) in LoraLists)
        {
            if (input.TryGet(param, out List<string> values) && values.Count > loraCount)
            {
                throw new ArgumentException($"swarmInput.{key} has {values.Count} entries for {loraCount} LoRAs.");
            }
        }
        try
        {
            input.ApplySpecialLogic();
        }
        catch (Exception ex)
        {
            throw new ArgumentException(ex.Message, ex);
        }
        if (sendPromptImages && input.TryGet(T2IParamTypes.PromptImages, out List<Image> images))
        {
            for (int i = 0; i < images.Count; i++)
            {
                if (images[i].Type.MetaType != MediaMetaType.Image)
                {
                    throw new ArgumentException($"Prompt Image {i + 1} is {images[i].Type.MimeType}, not a still image.");
                }
                context.PromptImages.Add(new BackendSchema.MediaContent
                {
                    Data = images[i].AsBase64,
                    MediaType = images[i].Type.MimeType,
                    Label = $"Image {i + 1}"
                });
            }
        }
        if (sendActiveModelContext)
        {
            context.ActiveModel = ActiveModel(input);
        }
    }

    /// <summary>The active model stack of a parsed input. <see cref="T2IParamInput.ApplySpecialLogic"/> has already reconciled the LoRA lists to the LoRA count (T2IParamInput.cs:64-86: a missing weight is "1", a missing text-encoder weight is the model weight).</summary>
    private static BackendSchema.ActiveModelContext ActiveModel(T2IParamInput input)
    {
        BackendSchema.ActiveModelContext active = new();
        if (input.TryGet(T2IParamTypes.Model, out T2IModel baseModel))
        {
            active.BaseModel = Metadata(baseModel);
        }
        if (!input.TryGet(T2IParamTypes.Loras, out List<string> names))
        {
            return active;
        }
        List<string> weights = input.Get(T2IParamTypes.LoraWeights, []);
        List<string> tencWeights = input.Get(T2IParamTypes.LoraTencWeights, []);
        List<string> confinements = input.Get(T2IParamTypes.LoraSectionConfinement, []);
        T2IModelHandler loraHandler = Program.T2IModelSets["LoRA"];
        for (int i = 0; i < names.Count; i++)
        {
            T2IModel lora = loraHandler.GetModel(names[i]) ?? throw new ArgumentException($"LoRA '{names[i]}' is not in SwarmUI's LoRA list.");
            double weight = ParseNumber(weights[i], $"LoRA '{names[i]}' weight");
            int scopeId = i < confinements.Count ? ParseInteger(confinements[i], $"LoRA '{names[i]}' section confinement") : 0;
            BackendSchema.ModelMetadata metadata = Metadata(lora);
            active.Loras.Add(new BackendSchema.ActiveLora
            {
                Name = metadata.Name,
                Title = metadata.Title,
                Architecture = metadata.Architecture,
                Class = metadata.Class,
                CompatClass = metadata.CompatClass,
                Description = metadata.Description,
                UsageHint = metadata.UsageHint,
                TriggerPhrase = metadata.TriggerPhrase,
                Tags = metadata.Tags,
                Weight = weight,
                TextEncoderWeight = i < tencWeights.Count ? ParseNumber(tencWeights[i], $"LoRA '{names[i]}' text encoder weight") : weight,
                ScopeId = scopeId,
                Scope = ScopeName(scopeId)
            });
        }
        return active;
    }

    /// <summary>The fields SwarmUI's own model description exposes (<see cref="T2IModel.ToNetObject"/>).</summary>
    private static BackendSchema.ModelMetadata Metadata(T2IModel model)
    {
        return new BackendSchema.ModelMetadata
        {
            Name = model.Name,
            Title = model.Metadata?.Title,
            Architecture = model.ModelClass?.ID,
            Class = model.ModelClass?.Name,
            CompatClass = model.ModelClass?.CompatClass?.ID,
            Description = model.Description,
            UsageHint = model.Metadata?.UsageHint,
            TriggerPhrase = model.Metadata?.TriggerPhrase,
            Tags = model.Metadata?.Tags is string[] tags ? [.. tags] : []
        };
    }

    /// <summary>Parses a number the way SwarmUI's workflow generator reads LoRA weights (invariant culture), rejecting anything that is not a finite number.</summary>
    private static double ParseNumber(string text, string what)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            throw new ArgumentException($"{what} is '{text}', which is not a number.");
        }
        return value;
    }

    /// <summary>Parses an integer section id, rejecting anything else.</summary>
    private static int ParseInteger(string text, string what)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ArgumentException($"{what} is '{text}', which is not an integer.");
        }
        return value;
    }
}
