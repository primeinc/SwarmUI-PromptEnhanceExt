using Newtonsoft.Json.Linq;

namespace PromptEnhance.WebAPI;

/// <summary>Strict parser for the provider-independent context payload collected from SwarmUI.</summary>
public static class PromptContextParser
{
    /// <summary>Parses an optional context object. Every present container and entry is type-checked so malformed requested context cannot be silently discarded.</summary>
    public static BackendSchema.PromptContext Parse(JToken raw)
    {
        BackendSchema.PromptContext context = new();
        if (raw == null)
        {
            return context;
        }

        JObject root = RequireObject(raw, "context");

        if (TryGet(root, "promptImages", out JToken promptImagesRaw))
        {
            JArray promptImages = RequireArray(promptImagesRaw, "context.promptImages");
            for (int i = 0; i < promptImages.Count; i++)
            {
                context.PromptImages.Add(ParseMedia(promptImages[i], $"Image {i + 1}", $"context.promptImages[{i}]"));
            }
        }

        if (TryGet(root, "pastGenerations", out JToken pastGenerationsRaw))
        {
            JArray pastGenerations = RequireArray(pastGenerationsRaw, "context.pastGenerations");
            for (int generationIndex = 0; generationIndex < pastGenerations.Count; generationIndex++)
            {
                string path = $"context.pastGenerations[{generationIndex}]";
                JObject generationRaw = RequireObject(pastGenerations[generationIndex], path);
                string requestId = RequireString(generationRaw, "requestId", path, allowEmpty: false);
                string prompt = RequireString(generationRaw, "prompt", path, allowEmpty: true);
                JArray outputs = RequireArray(RequireProperty(generationRaw, "outputs", path), $"{path}.outputs");
                if (outputs.Count == 0)
                {
                    throw new ArgumentException($"{path}.outputs must contain at least one generated output.");
                }

                BackendSchema.PastGeneration generation = new()
                {
                    RequestId = requestId,
                    Prompt = prompt
                };
                for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
                {
                    string outputPath = $"{path}.outputs[{outputIndex}]";
                    JObject outputRaw = RequireObject(outputs[outputIndex], outputPath);
                    generation.Outputs.Add(new BackendSchema.PastGenerationOutput
                    {
                        Image = ParseMedia(
                            RequireProperty(outputRaw, "image", outputPath),
                            $"Past Generation {generationIndex + 1} Output {outputIndex + 1}",
                            $"{outputPath}.image"),
                        Metadata = RequireString(outputRaw, "metadata", outputPath, allowEmpty: true)
                    });
                }
                context.PastGenerations.Add(generation);
            }
        }

        if (TryGet(root, "activeModel", out JToken activeModelRaw))
        {
            JObject activeRaw = RequireObject(activeModelRaw, "context.activeModel");
            context.ActiveModel = new BackendSchema.ActiveModelContext();

            if (TryGet(activeRaw, "baseModel", out JToken baseModelRaw) && baseModelRaw.Type != JTokenType.Null)
            {
                context.ActiveModel.BaseModel = ParseModel(baseModelRaw, "context.activeModel.baseModel");
            }

            JArray loras = RequireArray(RequireProperty(activeRaw, "loras", "context.activeModel"), "context.activeModel.loras");
            for (int i = 0; i < loras.Count; i++)
            {
                string path = $"context.activeModel.loras[{i}]";
                JObject loraRaw = RequireObject(loras[i], path);
                BackendSchema.ModelMetadata metadata = ParseModel(loraRaw, path);
                double weight = RequireFiniteNumber(loraRaw, "weight", path);
                double textEncoderWeight = RequireFiniteNumber(loraRaw, "textEncoderWeight", path);
                int scopeId = RequireInteger(loraRaw, "scopeId", path);
                string scope = RequireString(loraRaw, "scope", path, allowEmpty: false);
                context.ActiveModel.Loras.Add(new BackendSchema.ActiveLora
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
                    TextEncoderWeight = textEncoderWeight,
                    ScopeId = scopeId,
                    Scope = scope
                });
            }
        }

        return context;
    }

    private static BackendSchema.MediaContent ParseMedia(JToken raw, string fallbackLabel, string path)
    {
        JObject media = RequireObject(raw, path);
        string type = RequireString(media, "type", path, allowEmpty: false);
        if (type != "base64" && type != "url")
        {
            throw new ArgumentException($"{path}.type must be 'base64' or 'url'.");
        }
        string data = RequireString(media, "data", path, allowEmpty: false);
        string mediaType = OptionalString(media, "mediaType", path);
        if (type == "base64" && string.IsNullOrWhiteSpace(mediaType))
        {
            throw new ArgumentException($"{path}.mediaType is required for base64 image data.");
        }
        string label = OptionalString(media, "label", path) ?? fallbackLabel;
        return new BackendSchema.MediaContent
        {
            Type = type,
            Data = data,
            MediaType = mediaType,
            Label = label
        };
    }

    private static BackendSchema.ModelMetadata ParseModel(JToken raw, string path)
    {
        JObject model = RequireObject(raw, path);
        string name = RequireString(model, "name", path, allowEmpty: false);
        List<string> tags = [];
        if (TryGet(model, "tags", out JToken tagsRaw))
        {
            JArray rawTags = RequireArray(tagsRaw, $"{path}.tags");
            for (int i = 0; i < rawTags.Count; i++)
            {
                if (rawTags[i]?.Type != JTokenType.String)
                {
                    throw new ArgumentException($"{path}.tags[{i}] must be a string.");
                }
                tags.Add(rawTags[i].Value<string>());
            }
        }

        return new BackendSchema.ModelMetadata
        {
            Name = name,
            Title = OptionalString(model, "title", path),
            Architecture = OptionalString(model, "architecture", path),
            Class = OptionalString(model, "class", path),
            CompatClass = OptionalString(model, "compatClass", path) ?? OptionalString(model, "compat_class", path),
            Description = OptionalString(model, "description", path),
            UsageHint = OptionalString(model, "usageHint", path) ?? OptionalString(model, "usage_hint", path),
            TriggerPhrase = OptionalString(model, "triggerPhrase", path) ?? OptionalString(model, "trigger_phrase", path),
            Tags = tags
        };
    }

    private static bool TryGet(JObject obj, string key, out JToken value)
    {
        if (!obj.TryGetValue(key, out value))
        {
            return false;
        }
        if (value == null || value.Type == JTokenType.Null)
        {
            throw new ArgumentException($"{key} cannot be null when present.");
        }
        return true;
    }

    private static JToken RequireProperty(JObject obj, string key, string path)
    {
        if (!obj.TryGetValue(key, out JToken value) || value == null || value.Type == JTokenType.Null)
        {
            throw new ArgumentException($"{path}.{key} is required.");
        }
        return value;
    }

    private static JObject RequireObject(JToken raw, string path)
    {
        if (raw is not JObject obj)
        {
            throw new ArgumentException($"{path} must be an object.");
        }
        return obj;
    }

    private static JArray RequireArray(JToken raw, string path)
    {
        if (raw is not JArray array)
        {
            throw new ArgumentException($"{path} must be an array.");
        }
        return array;
    }

    private static string RequireString(JObject obj, string key, string path, bool allowEmpty)
    {
        JToken raw = RequireProperty(obj, key, path);
        if (raw.Type != JTokenType.String)
        {
            throw new ArgumentException($"{path}.{key} must be a string.");
        }
        string value = raw.Value<string>();
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{path}.{key} cannot be empty.");
        }
        return value;
    }

    private static string OptionalString(JObject obj, string key, string path)
    {
        if (!obj.TryGetValue(key, out JToken raw) || raw == null || raw.Type == JTokenType.Null)
        {
            return null;
        }
        if (raw.Type != JTokenType.String)
        {
            throw new ArgumentException($"{path}.{key} must be a string when present.");
        }
        return raw.Value<string>();
    }

    private static double RequireFiniteNumber(JObject obj, string key, string path)
    {
        JToken raw = RequireProperty(obj, key, path);
        if (raw.Type != JTokenType.Integer && raw.Type != JTokenType.Float)
        {
            throw new ArgumentException($"{path}.{key} must be a number.");
        }
        double value = raw.Value<double>();
        if (!double.IsFinite(value))
        {
            throw new ArgumentException($"{path}.{key} must be finite.");
        }
        return value;
    }

    private static int RequireInteger(JObject obj, string key, string path)
    {
        JToken raw = RequireProperty(obj, key, path);
        if (raw.Type != JTokenType.Integer)
        {
            throw new ArgumentException($"{path}.{key} must be an integer.");
        }
        try
        {
            return raw.Value<int>();
        }
        catch (Exception ex) when (ex is OverflowException or FormatException)
        {
            throw new ArgumentException($"{path}.{key} is outside the supported integer range.");
        }
    }
}
