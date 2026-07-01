using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Utils;

namespace PromptEnhance.WebAPI;

/// <summary>The single source of truth for PromptEnhance configuration. All settings live in one flat object,
/// persisted as JSON under the calling user's own generic-data key <c>promptenhance/config</c> (per-user, so one
/// user's backend URL / model / prompt never leaks to another).
/// Load merges stored values over <see cref="Defaults"/> so a newly added field always has a sane default.</summary>
public class SessionSettings
{
    private const string SETTINGS_KEY = "promptenhance";
    private const string SETTINGS_SUBKEY = "config";

    /// <summary>The complete default configuration. Every configurable knob appears here exactly once.</summary>
    public static JObject Defaults => new()
    {
        // Base URL of the OpenAI-compatible server. Either a server root or a URL ending in /v1 is accepted;
        // the backend client normalizes it so the owned seams resolve to {base}/v1/models and {base}/v1/chat/completions.
        ["baseUrl"] = "http://localhost:11434",
        ["model"] = "",
        ["timeoutSeconds"] = 60,
        ["systemPrompt"] = "You are a prompt enhancer for text-to-image generation. Rewrite the user's prompt into a single, richly detailed image-generation prompt. Reply with only the enhanced prompt, no preamble or explanation.",
        ["temperature"] = 0.7,
        ["maxTokens"] = 1024,
        ["sendSelectedImage"] = false,
        // How an enhancement result is applied to the prompt box. One of: preview | append | replace_with_restore.
        ["replaceMode"] = "preview"
    };

    /// <summary>The setting keys that are known/persisted. Anything else in an incoming payload is ignored,
    /// so the config surface can never silently grow.</summary>
    private static readonly string[] KnownKeys =
    [
        "baseUrl", "model", "timeoutSeconds", "systemPrompt", "temperature", "maxTokens", "sendSelectedImage", "replaceMode"
    ];

    /// <summary>Loads the current settings (defaults overlaid with any stored values). Never throws to the caller —
    /// returns a structured error payload instead.</summary>
    public static Task<JObject> GetPromptEnhanceSettings(Session session)
    {
        try
        {
            JObject settings = Defaults;
            string stored = session.User.GetGenericData(SETTINGS_KEY, SETTINGS_SUBKEY);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                JObject storedObj = JObject.Parse(stored);
                foreach (string key in KnownKeys)
                {
                    if (storedObj[key] != null && storedObj[key].Type != JTokenType.Null)
                    {
                        settings[key] = storedObj[key];
                    }
                }
            }
            return Task.FromResult(PromptEnhanceAPI.CreateSettingsResponse(settings));
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Failed to load settings: {ex.Message}");
            return Task.FromResult(PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, $"Failed to load settings: {ex.Message}"));
        }
    }

    /// <summary>Saves the provided settings (only known keys are persisted). Returns the merged result so the UI can
    /// confirm exactly what was stored.</summary>
    public static Task<JObject> SavePromptEnhanceSettings(JObject rawInput, Session session)
    {
        try
        {
            JObject incoming = rawInput?["settings"] as JObject;
            if (incoming == null)
            {
                return Task.FromResult(PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "No settings object provided."));
            }
            JObject validationError = ValidateSettings(incoming);
            if (validationError != null)
            {
                return Task.FromResult(validationError);
            }
            JObject merged = Defaults;
            string stored = session.User.GetGenericData(SETTINGS_KEY, SETTINGS_SUBKEY);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                JObject storedObj = JObject.Parse(stored);
                foreach (string key in KnownKeys)
                {
                    if (storedObj[key] != null && storedObj[key].Type != JTokenType.Null)
                    {
                        merged[key] = storedObj[key];
                    }
                }
            }
            foreach (string key in KnownKeys)
            {
                if (incoming[key] != null && incoming[key].Type != JTokenType.Null)
                {
                    merged[key] = incoming[key];
                }
            }
            session.User.SaveGenericData(SETTINGS_KEY, SETTINGS_SUBKEY, merged.ToString());
            return Task.FromResult(PromptEnhanceAPI.CreateSettingsResponse(merged));
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Failed to save settings: {ex.Message}");
            return Task.FromResult(PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, $"Failed to save settings: {ex.Message}"));
        }
    }

    /// <summary>Validates an incoming (possibly partial) settings object before it is persisted. Only keys that are
    /// present are checked; an absent key keeps its already-valid stored or default value. Returns a structured error
    /// payload (<c>success:false</c>) describing the first offending field, or null when every present value is
    /// well-typed and in range. This is the single guard that stops an out-of-range or wrong-typed value (for example
    /// <c>timeoutSeconds:0</c> which cancels every request immediately, a negative timeout which throws inside the
    /// backend client's CancellationTokenSource, or a non-numeric temperature which throws when the client reads it)
    /// from ever being stored.</summary>
    public static JObject ValidateSettings(JObject incoming)
    {
        JToken baseUrl = incoming["baseUrl"];
        if (baseUrl != null && baseUrl.Type != JTokenType.Null)
        {
            if (baseUrl.Type != JTokenType.String || string.IsNullOrWhiteSpace(baseUrl.Value<string>()))
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Base URL must be a non-empty string.");
            }
        }
        JToken timeoutSeconds = incoming["timeoutSeconds"];
        if (timeoutSeconds != null && timeoutSeconds.Type != JTokenType.Null)
        {
            if (timeoutSeconds.Type != JTokenType.Integer || timeoutSeconds.Value<long>() < 1)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Timeout (seconds) must be a whole number of at least 1.");
            }
        }
        JToken maxTokens = incoming["maxTokens"];
        if (maxTokens != null && maxTokens.Type != JTokenType.Null)
        {
            if (maxTokens.Type != JTokenType.Integer || maxTokens.Value<long>() < 1)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Max tokens must be a whole number of at least 1.");
            }
        }
        JToken temperature = incoming["temperature"];
        if (temperature != null && temperature.Type != JTokenType.Null)
        {
            if (temperature.Type != JTokenType.Float && temperature.Type != JTokenType.Integer)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Temperature must be a number between 0 and 2.");
            }
            double temperatureValue = temperature.Value<double>();
            if (temperatureValue < 0 || temperatureValue > 2)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Temperature must be a number between 0 and 2.");
            }
        }
        JToken replaceMode = incoming["replaceMode"];
        if (replaceMode != null && replaceMode.Type != JTokenType.Null)
        {
            string mode = replaceMode.Type == JTokenType.String ? replaceMode.Value<string>() : null;
            if (mode != "preview" && mode != "append" && mode != "replace_with_restore")
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Replace mode must be one of: preview, append, replace_with_restore.");
            }
        }
        return null;
    }

    /// <summary>Resets settings to <see cref="Defaults"/> and persists them.</summary>
    public static Task<JObject> ResetPromptEnhanceSettings(Session session)
    {
        try
        {
            JObject settings = Defaults;
            session.User.SaveGenericData(SETTINGS_KEY, SETTINGS_SUBKEY, settings.ToString());
            return Task.FromResult(PromptEnhanceAPI.CreateSettingsResponse(settings));
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Failed to reset settings: {ex.Message}");
            return Task.FromResult(PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, $"Failed to reset settings: {ex.Message}"));
        }
    }
}
