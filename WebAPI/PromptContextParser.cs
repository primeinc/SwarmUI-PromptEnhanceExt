using Newtonsoft.Json.Linq;

namespace PromptEnhance.WebAPI;

/// <summary>Strict parser for the provider-independent context payload collected from SwarmUI.</summary>
public static class PromptContextParser
{
    public static BackendSchema.PromptContext Parse(JObject raw)
    {
        BackendSchema.PromptContext context = new();
        if (raw == null)
        {
            return context;
        }

        if (raw["promptImages"] is JArray promptImages)
        {
            for (int i = 0; i < promptImages.Count; i++)
            {
                context.PromptImages.Add(ParseMedia(promptImages[i], $"Image {i + 1}"));
            }
        }

        if (raw["pastGenerations"] is JArray pastGenerations)
        {
            for (int generationIndex = 0; generationIndex < pastGenerations.Count; generationIndex++)
            {
                JToken generationRaw = pastGenerations[generationIndex];
                BackendSchema.PastGeneration generation = new()
                {
                    RequestId = generationRaw?["requestId"]?.ToString(),
                    Prompt = generationRaw?["prompt"]?.ToString()
                };
                if (generationRaw?["outputs"] is JArray outputs)
                {
                    for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
                    {
                        JToken outputRaw = outputs[outputIndex];
                        generation.Outputs.Add(new BackendSchema.PastGenerationOutput
                        {
                            Image = ParseMedia(outputRaw?["image"], $"Past Generation {generationIndex + 1} Output {outputIndex + 1}"),
                            Metadata = outputRaw?["metadata"]?.ToString()
                        });
                    }
                }
                context.PastGenerations.Add(generation);
            }
        }

        if (raw["activeModel"] is JObject activeRaw)
        {
            context.ActiveModel = new BackendSchema.ActiveModelContext
            {
                BaseModel = ParseModel(activeRaw["baseModel"])
            };
            if (activeRaw["loras"] is JArray loras)
            {
                foreach (JToken loraRaw in loras)
                {
                    BackendSchema.ModelMetadata metadata = ParseModel(loraRaw) ?? new BackendSchema.ModelMetadata();
                    double weight = loraRaw?["weight"]?.Value<double?>() ?? 1;
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
                        TextEncoderWeight = loraRaw?["textEncoderWeight"]?.Value<double?>() ?? weight,
                        ScopeId = loraRaw?["scopeId"]?.Value<int?>() ?? 0,
                        Scope = loraRaw?["scope"]?.ToString() ?? "Global"
                    });
                }
            }
        }

        return context;
    }

    private static BackendSchema.MediaContent ParseMedia(JToken raw, string label)
    {
        string data = raw?["data"]?.ToString();
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new ArgumentException($"{label} was attached but carried no image data.");
        }
        return new BackendSchema.MediaContent
        {
            Type = raw?["type"]?.ToString() ?? "base64",
            Data = data,
            MediaType = raw?["mediaType"]?.ToString(),
            Label = raw?["label"]?.ToString() ?? label
        };
    }

    private static BackendSchema.ModelMetadata ParseModel(JToken raw)
    {
        if (raw == null || raw.Type == JTokenType.Null)
        {
            return null;
        }
        List<string> tags = [];
        if (raw["tags"] is JArray rawTags)
        {
            foreach (JToken tag in rawTags)
            {
                string value = tag?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    tags.Add(value);
                }
            }
        }
        return new BackendSchema.ModelMetadata
        {
            Name = raw["name"]?.ToString(),
            Title = raw["title"]?.ToString(),
            Architecture = raw["architecture"]?.ToString(),
            Class = raw["class"]?.ToString(),
            CompatClass = raw["compatClass"]?.ToString() ?? raw["compat_class"]?.ToString(),
            Description = raw["description"]?.ToString(),
            UsageHint = raw["usageHint"]?.ToString() ?? raw["usage_hint"]?.ToString(),
            TriggerPhrase = raw["triggerPhrase"]?.ToString() ?? raw["trigger_phrase"]?.ToString(),
            Tags = tags
        };
    }
}
