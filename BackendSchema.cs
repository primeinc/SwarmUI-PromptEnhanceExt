namespace PromptEnhance;

/// <summary>Builds the OpenAI-compatible `/v1/chat/completions` request body.</summary>
public static class BackendSchema
{
    /// <summary>One image attachment.</summary>
    public class MediaContent
    {
        /// <summary>"base64" (data URI is synthesized here) or "url" (passed through as-is).</summary>
        public string Type;

        /// <summary>The base64 image bytes, or the URL for a "url" part.</summary>
        public string Data;

        /// <summary>MIME type for base64 parts; defaults to image/jpeg when absent.</summary>
        public string MediaType;

        /// <summary>Stable human-facing identity, such as "Image 1" or "Past Generation 1 Output 2".</summary>
        public string Label;
    }

    /// <summary>One historical output paired with the raw Swarm generation metadata that produced it.</summary>
    public class PastGenerationOutput
    {
        public MediaContent Image;
        public string Metadata;
    }

    /// <summary>One prior Swarm generation request and all of its outputs.</summary>
    public class PastGeneration
    {
        public string RequestId;
        public string Prompt;
        public List<PastGenerationOutput> Outputs = [];
    }

    /// <summary>Semantic metadata for the selected base model or one active adapter.</summary>
    public class ModelMetadata
    {
        public string Name;
        public string Title;
        public string Architecture;
        public string Class;
        public string CompatClass;
        public string Description;
        public string UsageHint;
        public string TriggerPhrase;
        public List<string> Tags = [];
    }

    /// <summary>One active LoRA plus the effective runtime values Swarm will apply.</summary>
    public class ActiveLora : ModelMetadata
    {
        public double Weight = 1;
        public double TextEncoderWeight = 1;
        public int ScopeId;
        public string Scope = "Global";
    }

    /// <summary>The selected generation stack that affects how a prompt should be written.</summary>
    public class ActiveModelContext
    {
        public ModelMetadata BaseModel;
        public List<ActiveLora> Loras = [];
    }

    /// <summary>Canonical multimodal context collected from SwarmUI before provider-specific translation.</summary>
    public class PromptContext
    {
        public List<MediaContent> PromptImages = [];
        public List<PastGeneration> PastGenerations = [];
        public ActiveModelContext ActiveModel;

        public bool HasImages => PromptImages.Count > 0 || PastGenerations.Any(g => g.Outputs.Count > 0);
    }

    private static object ImagePart(MediaContent media)
    {
        string url = media.Type == "base64"
            ? $"data:{(string.IsNullOrWhiteSpace(media.MediaType) ? "image/jpeg" : media.MediaType)};base64,{media.Data}"
            : media.Data;
        return new { type = "image_url", image_url = new { url } };
    }

    private static void AddLabeledImage(List<object> parts, string label, MediaContent image)
    {
        parts.Add(new { type = "text", text = label });
        parts.Add(ImagePart(image));
    }

    private static string ModelText(ModelMetadata model)
    {
        if (model == null)
        {
            return "(none)";
        }
        List<string> lines = [$"Name: {model.Name ?? "(unknown)"}"];
        if (!string.IsNullOrWhiteSpace(model.Title)) lines.Add($"Title: {model.Title}");
        if (!string.IsNullOrWhiteSpace(model.Architecture)) lines.Add($"Architecture: {model.Architecture}");
        if (!string.IsNullOrWhiteSpace(model.Class)) lines.Add($"Class: {model.Class}");
        if (!string.IsNullOrWhiteSpace(model.CompatClass)) lines.Add($"Compatibility class: {model.CompatClass}");
        if (!string.IsNullOrWhiteSpace(model.TriggerPhrase)) lines.Add($"Trigger phrase: {model.TriggerPhrase}");
        if (!string.IsNullOrWhiteSpace(model.UsageHint)) lines.Add($"Usage hint: {model.UsageHint}");
        if (!string.IsNullOrWhiteSpace(model.Description)) lines.Add($"Description: {model.Description}");
        if (model.Tags is { Count: > 0 }) lines.Add($"Tags: {string.Join(", ", model.Tags)}");
        return string.Join("\n", lines);
    }

    /// <summary>Assembles provider-independent Swarm context into one ordered OpenAI-compatible multimodal user message.</summary>
    public static object BuildChatRequest(string model, string systemPrompt, string userText, List<MediaContent> media, double temperature, int maxTokens, PromptContext context = null)
    {
        List<object> messages = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        context ??= new PromptContext();
        bool hasImages = media is { Count: > 0 } || context.HasImages;
        bool hasStructuredContext = context.PromptImages.Count > 0 || context.PastGenerations.Count > 0 || context.ActiveModel != null;

        if (!hasStructuredContext)
        {
            if (media is { Count: > 0 })
            {
                List<object> legacyParts = [];
                foreach (MediaContent image in media)
                {
                    legacyParts.Add(ImagePart(image));
                }
                legacyParts.Add(new { type = "text", text = userText });
                messages.Add(new { role = "user", content = legacyParts });
            }
            else
            {
                messages.Add(new { role = "user", content = userText });
            }
        }
        else
        {
            List<object> parts = [];
            parts.Add(new
            {
                type = "text",
                text = "The following context comes from SwarmUI. Keep CURRENT PROMPT IMAGES, PAST GENERATIONS, and ACTIVE MODEL CONTEXT semantically separate. Image N always refers only to CURRENT PROMPT IMAGES. Use past generations as visual feedback, not as current reference images."
            });

            if (context.PromptImages.Count > 0)
            {
                parts.Add(new { type = "text", text = "CURRENT PROMPT IMAGES" });
                foreach (MediaContent image in context.PromptImages)
                {
                    AddLabeledImage(parts, image.Label ?? "Prompt image", image);
                }
            }

            if (media is { Count: > 0 })
            {
                parts.Add(new { type = "text", text = "LEGACY SELECTED IMAGE CONTEXT" });
                for (int i = 0; i < media.Count; i++)
                {
                    AddLabeledImage(parts, media[i].Label ?? $"Selected Image {i + 1}", media[i]);
                }
            }

            if (context.PastGenerations.Count > 0)
            {
                parts.Add(new { type = "text", text = "PAST GENERATIONS (oldest to newest). Compare each prompt with its outputs to diagnose what is going wrong." });
                for (int generationIndex = 0; generationIndex < context.PastGenerations.Count; generationIndex++)
                {
                    PastGeneration generation = context.PastGenerations[generationIndex];
                    parts.Add(new { type = "text", text = $"Past Generation {generationIndex + 1}\nRequest ID: {generation.RequestId}\nPrompt: {generation.Prompt}" });
                    foreach (PastGenerationOutput output in generation.Outputs)
                    {
                        AddLabeledImage(parts, output.Image.Label ?? $"Past Generation {generationIndex + 1} Output", output.Image);
                        if (!string.IsNullOrWhiteSpace(output.Metadata))
                        {
                            parts.Add(new { type = "text", text = $"Raw Swarm metadata for {output.Image.Label}:\n{output.Metadata}" });
                        }
                    }
                }
            }

            if (context.ActiveModel != null)
            {
                List<string> modelLines = ["ACTIVE MODEL CONTEXT", "Base model:", ModelText(context.ActiveModel.BaseModel)];
                for (int i = 0; i < context.ActiveModel.Loras.Count; i++)
                {
                    ActiveLora lora = context.ActiveModel.Loras[i];
                    modelLines.Add($"Active LoRA {i + 1}:\n{ModelText(lora)}\nModel weight: {lora.Weight}\nText encoder weight: {lora.TextEncoderWeight}\nScope: {lora.Scope} ({lora.ScopeId})");
                }
                modelLines.Add("Account for the active generation stack when rewriting. Preserve required trigger phrases when appropriate. Avoid needlessly restating concepts already strongly supplied by active adapters, and avoid introducing prompt language that conflicts with known active conditioning.");
                parts.Add(new { type = "text", text = string.Join("\n\n", modelLines) });
            }

            parts.Add(new { type = "text", text = $"CURRENT PROMPT TO ENHANCE:\n{userText}" });
            messages.Add(new { role = "user", content = parts });
        }

        return new
        {
            model,
            messages = messages.ToArray(),
            temperature,
            max_tokens = maxTokens,
            stream = false
        };
    }
}
