using System.Globalization;

namespace PromptEnhance;

/// <summary>Builds the OpenAI-compatible `/v1/chat/completions` request body.</summary>
public static class BackendSchema
{
    /// <summary>One base64 image sent to the model.</summary>
    public class MediaContent
    {
        /// <summary>The base64 image bytes.</summary>
        public string Data;

        /// <summary>The image MIME type, for example "image/png".</summary>
        public string MediaType;

        /// <summary>Stable human-facing identity, such as "Image 1" or "Past Generation 1 Output 2".</summary>
        public string Label;
    }

    /// <summary>One recorded output of a past SwarmUI generation request.</summary>
    public class PastGenerationOutput
    {
        /// <summary>The output image.</summary>
        public MediaContent Image;

        /// <summary>The prompt SwarmUI generated this output from, after wildcard and random-tag resolution.</summary>
        public string Prompt;

        /// <summary>The raw SwarmUI metadata saved in the output file; null when SwarmUI saved none.</summary>
        public string Metadata;
    }

    /// <summary>One past SwarmUI generation request and its saved outputs.</summary>
    public class PastGeneration
    {
        /// <summary>The outputs, in the order SwarmUI produced them.</summary>
        public List<PastGenerationOutput> Outputs = [];
    }

    /// <summary>Semantic metadata for the selected base model or one active LoRA, as SwarmUI's model registry holds it.</summary>
    public class ModelMetadata
    {
        /// <summary>SwarmUI's model name.</summary>
        public string Name;

        /// <summary>Metadata title, if set.</summary>
        public string Title;

        /// <summary>Model class id, for example "stable-diffusion-xl-v1-base".</summary>
        public string Architecture;

        /// <summary>Model class display name.</summary>
        public string Class;

        /// <summary>Compatibility class id.</summary>
        public string CompatClass;

        /// <summary>Description text, if any.</summary>
        public string Description;

        /// <summary>Usage hint, if any.</summary>
        public string UsageHint;

        /// <summary>Trigger phrase, if any.</summary>
        public string TriggerPhrase;

        /// <summary>Metadata tags; empty when the model has none.</summary>
        public List<string> Tags = [];
    }

    /// <summary>One active LoRA plus the values SwarmUI applies to it.</summary>
    public class ActiveLora : ModelMetadata
    {
        /// <summary>Model weight.</summary>
        public double Weight;

        /// <summary>Text-encoder weight.</summary>
        public double TextEncoderWeight;

        /// <summary>Section confinement id; 0 is global.</summary>
        public int ScopeId;

        /// <summary>Display name of <see cref="ScopeId"/>.</summary>
        public string Scope;
    }

    /// <summary>The selected base model and active LoRAs.</summary>
    public class ActiveModelContext
    {
        /// <summary>The base model, or null when none is selected.</summary>
        public ModelMetadata BaseModel;

        /// <summary>Active LoRAs, in SwarmUI's parameter order.</summary>
        public List<ActiveLora> Loras = [];
    }

    /// <summary>Everything besides the prompt text that an enhance request sends to the model.</summary>
    public class PromptContext
    {
        /// <summary>The Generate tab's current Prompt Images, in order.</summary>
        public List<MediaContent> PromptImages = [];

        /// <summary>Past generation requests, oldest first.</summary>
        public List<PastGeneration> PastGenerations = [];

        /// <summary>The active model stack, or null when not requested.</summary>
        public ActiveModelContext ActiveModel;

        /// <summary>True when any image is attached.</summary>
        public bool HasImages => PromptImages.Count > 0 || PastGenerations.Exists(g => g.Outputs.Count > 0);

        /// <summary>True when there is anything to send besides the prompt text.</summary>
        public bool IsEmpty => PromptImages.Count == 0 && PastGenerations.Count == 0 && ActiveModel == null;
    }

    private static object TextPart(string text)
    {
        return new { type = "text", text };
    }

    private static object ImagePart(MediaContent media)
    {
        return new { type = "image_url", image_url = new { url = $"data:{media.MediaType};base64,{media.Data}" } };
    }

    private static string ModelText(ModelMetadata model)
    {
        if (model == null)
        {
            return "(none)";
        }
        List<string> lines = [$"Name: {model.Name}"];
        void add(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add($"{label}: {value}");
            }
        }
        add("Title", model.Title);
        add("Architecture", model.Architecture);
        add("Class", model.Class);
        add("Compatibility class", model.CompatClass);
        add("Trigger phrase", model.TriggerPhrase);
        add("Usage hint", model.UsageHint);
        add("Description", model.Description);
        if (model.Tags.Count > 0)
        {
            lines.Add($"Tags: {string.Join(", ", model.Tags)}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>Assembles the messages array: an optional system message (omitted when blank), then one user message. The user message is the plain prompt when <paramref name="context"/> is empty, else an ordered multimodal part list that keeps current prompt images, past generations, and the active model stack in separate labeled sections.</summary>
    public static object BuildChatRequest(string model, string systemPrompt, string userText, double temperature, int maxTokens, PromptContext context)
    {
        List<object> messages = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }
        if (context.IsEmpty)
        {
            messages.Add(new { role = "user", content = userText });
        }
        else
        {
            List<object> parts = [TextPart("The following context comes from SwarmUI. Keep CURRENT PROMPT IMAGES, PAST GENERATIONS, and ACTIVE MODEL CONTEXT semantically separate. Image N always refers only to CURRENT PROMPT IMAGES. Use past generations as visual feedback, not as current reference images.")];
            if (context.PromptImages.Count > 0)
            {
                parts.Add(TextPart("CURRENT PROMPT IMAGES"));
                foreach (MediaContent image in context.PromptImages)
                {
                    parts.Add(TextPart(image.Label));
                    parts.Add(ImagePart(image));
                }
            }
            if (context.PastGenerations.Count > 0)
            {
                parts.Add(TextPart("PAST GENERATIONS (oldest to newest). Compare each output's prompt with the output to diagnose what is going wrong."));
                foreach (PastGeneration generation in context.PastGenerations)
                {
                    foreach (PastGenerationOutput output in generation.Outputs)
                    {
                        parts.Add(TextPart($"{output.Image.Label}\nPrompt: {output.Prompt}"));
                        parts.Add(ImagePart(output.Image));
                        if (output.Metadata != null)
                        {
                            parts.Add(TextPart($"Raw SwarmUI metadata for {output.Image.Label}:\n{output.Metadata}"));
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
                    modelLines.Add($"Active LoRA {i + 1}:\n{ModelText(lora)}\nModel weight: {lora.Weight.ToString(CultureInfo.InvariantCulture)}\nText encoder weight: {lora.TextEncoderWeight.ToString(CultureInfo.InvariantCulture)}\nScope: {lora.Scope} ({lora.ScopeId})");
                }
                modelLines.Add("Account for the active generation stack when rewriting. Preserve required trigger phrases when appropriate. Avoid needlessly restating concepts already strongly supplied by active adapters, and avoid introducing prompt language that conflicts with known active conditioning.");
                parts.Add(TextPart(string.Join("\n\n", modelLines)));
            }
            parts.Add(TextPart($"CURRENT PROMPT TO ENHANCE:\n{userText}"));
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
