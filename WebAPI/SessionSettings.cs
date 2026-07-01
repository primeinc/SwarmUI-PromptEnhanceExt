using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Utils;

namespace PromptEnhance.WebAPI;

/// <summary>
/// Settings persistence: the single server-side source of truth for the
/// eight-key settings schema, stored per-user through SwarmUI's own generic
/// user-data store (User.GetGenericData/SaveGenericData). Reads merge stored
/// values over <see cref="Defaults"/> key-by-key, so unknown or missing keys
/// can never corrupt the effective settings.
/// </summary>
public class SessionSettings
{
    private const string SETTINGS_KEY = "promptenhance";
    private const string SETTINGS_SUBKEY = "config";

    /// <summary>
    /// The canonical defaults. Frontend/contracts.ts mirrors these verbatim
    /// (SettingsDefaultsParityTests pins the systemPrompt text); a fresh
    /// profile works against a local Ollama with zero configuration except
    /// picking a model.
    /// </summary>
    public static JObject Defaults => new()
    {
        ["baseUrl"] = "http://localhost:11434",
        ["model"] = "",
        ["timeoutSeconds"] = 60,
        ["systemPrompt"] = "You are a prompt enhancer for text-to-image generation. Rewrite the user's prompt into a single, richly detailed image-generation prompt. Reply with only the enhanced prompt, no preamble or explanation.",
        ["temperature"] = 0.7,
        ["maxTokens"] = 1024,
        ["sendSelectedImage"] = false,
        ["replaceMode"] = "preview"
    };

    private static readonly string[] KnownKeys =
    [
        "baseUrl", "model", "timeoutSeconds", "systemPrompt", "temperature", "maxTokens", "sendSelectedImage", "replaceMode"
    ];

    /// <summary>API route: returns the user's effective settings (stored values merged over defaults).</summary>
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

    /// <summary>
    /// API route: validates then persists a partial settings object. The merge
    /// order is defaults ← previously stored ← incoming, per known key, so a
    /// partial save never erases unrelated settings and unknown keys are
    /// dropped at the boundary.
    /// </summary>
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

    /// <summary>
    /// Schema validation for an incoming partial settings object. Integer
    /// fields are bounded to [1, int.MaxValue] as long values — an over-range
    /// stored value would otherwise overflow later Value&lt;int?&gt; reads into an
    /// unclassified 500. Returns null when valid, else a classified error response.
    /// </summary>
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
            if (timeoutSeconds.Type != JTokenType.Integer || timeoutSeconds.Value<long>() < 1 || timeoutSeconds.Value<long>() > int.MaxValue)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, $"Timeout (seconds) must be a whole number between 1 and {int.MaxValue}.");
            }
        }
        JToken maxTokens = incoming["maxTokens"];
        if (maxTokens != null && maxTokens.Type != JTokenType.Null)
        {
            if (maxTokens.Type != JTokenType.Integer || maxTokens.Value<long>() < 1 || maxTokens.Value<long>() > int.MaxValue)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, $"Max tokens must be a whole number between 1 and {int.MaxValue}.");
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

    /// <summary>API route: overwrites the user's stored settings with <see cref="Defaults"/> and returns them.</summary>
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
