using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Utils;

namespace PromptEnhance.WebAPI;

/// <summary>Settings persistence for the eight-key settings schema, stored per-user through User.GetGenericData/SaveGenericData. Reads merge stored values over <see cref="Defaults"/> key-by-key.</summary>
public class SessionSettings
{
    private const string SETTINGS_KEY = "promptenhance";
    private const string SETTINGS_SUBKEY = "config";
    private const string CORRUPT_BACKUP_SUBKEY = "config_corrupt_backup";

    /// <summary>Request timeout ceiling in seconds. Frontend/settings.ts mirrors this bound.</summary>
    public const int MaxTimeoutSeconds = 3600;

    /// <summary>The defaults, mirroring contracts/pe-contract.json.</summary>
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

    /// <summary>Parses the stored settings blob, treating unparseable data as absent.</summary>
    private static JObject TryParseStored(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }
        try
        {
            return JObject.Parse(stored);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            Logs.Warning($"[PromptEnhance] Stored settings are corrupt and will be ignored (defaults apply until the next save; the corrupt data is kept under the '{CORRUPT_BACKUP_SUBKEY}' subkey): {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads and parses the stored settings. Unparseable data is backed up once under <see cref="CORRUPT_BACKUP_SUBKEY"/> and <paramref name="recovered"/> is set.</summary>
    private static JObject ReadStored(Session session, out bool recovered)
    {
        string stored = session.User.GetGenericData(SETTINGS_KEY, SETTINGS_SUBKEY);
        JObject storedObj = TryParseStored(stored);
        recovered = storedObj == null && !string.IsNullOrWhiteSpace(stored);
        if (recovered)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(session.User.GetGenericData(SETTINGS_KEY, CORRUPT_BACKUP_SUBKEY)))
                {
                    session.User.SaveGenericData(SETTINGS_KEY, CORRUPT_BACKUP_SUBKEY, stored);
                }
            }
            catch (Exception ex)
            {
                Logs.Warning($"[PromptEnhance] Could not back up the corrupt settings blob: {ex.Message}");
            }
        }
        return storedObj;
    }

    /// <summary>API route: returns the user's effective settings (stored values merged over defaults).</summary>
    public static Task<JObject> GetPromptEnhanceSettings(Session session)
    {
        try
        {
            JObject settings = Defaults;
            JObject storedObj = ReadStored(session, out bool recovered);
            if (storedObj != null)
            {
                foreach (string key in KnownKeys)
                {
                    if (storedObj[key] != null && storedObj[key].Type != JTokenType.Null)
                    {
                        settings[key] = storedObj[key];
                    }
                }
            }
            JObject response = PromptEnhanceAPI.CreateSettingsResponse(settings);
            if (recovered)
            {
                response["recovered"] = true;
            }
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Failed to load settings: {ex.Message}");
            return Task.FromResult(PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, $"Failed to load settings: {ex.Message}"));
        }
    }

    /// <summary>API route: validates then persists a partial settings object. Merge order is defaults ← previously stored ← incoming, per known key; unknown keys are dropped.</summary>
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
            JObject storedObj = ReadStored(session, out bool recovered);
            if (storedObj != null)
            {
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
            JObject persistError = PersistVerified(session, merged.ToString());
            if (persistError != null)
            {
                return Task.FromResult(persistError);
            }
            JObject response = PromptEnhanceAPI.CreateSettingsResponse(merged);
            if (recovered)
            {
                response["recovered"] = true;
            }
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Failed to save settings: {ex.Message}");
            return Task.FromResult(PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, $"Failed to save settings: {ex.Message}"));
        }
    }

    /// <summary>Schema validation for an incoming partial settings object, covering every key in <see cref="KnownKeys"/>. Returns null when valid, else a classified error response.</summary>
    public static JObject ValidateSettings(JObject incoming)
    {
        JToken baseUrl = incoming["baseUrl"];
        if (baseUrl != null && baseUrl.Type != JTokenType.Null)
        {
            if (baseUrl.Type != JTokenType.String || string.IsNullOrWhiteSpace(baseUrl.Value<string>()))
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Base URL must be a non-empty string.");
            }
            if (BackendClient.NormalizeBaseUrl(baseUrl.Value<string>()) == null)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Base URL must be a valid http(s) URL (for example http://localhost:11434).");
            }
        }
        JToken model = incoming["model"];
        if (model != null && model.Type != JTokenType.Null)
        {
            if (model.Type != JTokenType.String)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Model must be a string (an empty string means no model is selected).");
            }
        }
        JToken timeoutSeconds = incoming["timeoutSeconds"];
        if (timeoutSeconds != null && timeoutSeconds.Type != JTokenType.Null)
        {
            if (timeoutSeconds.Type != JTokenType.Integer || timeoutSeconds.Value<long>() < 1 || timeoutSeconds.Value<long>() > MaxTimeoutSeconds)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, $"Timeout (seconds) must be a whole number between 1 and {MaxTimeoutSeconds}.");
            }
        }
        JToken systemPrompt = incoming["systemPrompt"];
        if (systemPrompt != null && systemPrompt.Type != JTokenType.Null)
        {
            if (systemPrompt.Type != JTokenType.String)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "System prompt must be a string.");
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
        JToken sendSelectedImage = incoming["sendSelectedImage"];
        if (sendSelectedImage != null && sendSelectedImage.Type != JTokenType.Null)
        {
            if (sendSelectedImage.Type != JTokenType.Boolean)
            {
                return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "Send selected image must be a boolean (true or false).");
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

    /// <summary>Writes the serialized settings through User.SaveGenericData and reads them back. Returns null on verified persistence, else a classified error response.</summary>
    private static JObject PersistVerified(Session session, string serialized)
    {
        session.User.SaveGenericData(SETTINGS_KEY, SETTINGS_SUBKEY, serialized);
        string stored = session.User.GetGenericData(SETTINGS_KEY, SETTINGS_SUBKEY);
        if (!PersistedMatches(stored, serialized))
        {
            return PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, "The server did not persist the settings (persistence is disabled or this account cannot save data).");
        }
        return null;
    }

    /// <summary>True when <paramref name="stored"/> represents the same settings that were just written: ordinal compare, then semantic JSON compare. A null read or unparseable data is treated as not persisted.</summary>
    private static bool PersistedMatches(string stored, string serialized)
    {
        if (stored == null)
        {
            return false;
        }
        if (string.Equals(stored, serialized, System.StringComparison.Ordinal))
        {
            return true;
        }
        try
        {
            return JToken.DeepEquals(JToken.Parse(stored), JToken.Parse(serialized));
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>API route: overwrites the user's stored settings with <see cref="Defaults"/> and returns them.</summary>
    public static Task<JObject> ResetPromptEnhanceSettings(Session session)
    {
        try
        {
            JObject settings = Defaults;
            JObject persistError = PersistVerified(session, settings.ToString());
            if (persistError != null)
            {
                return Task.FromResult(persistError);
            }
            return Task.FromResult(PromptEnhanceAPI.CreateSettingsResponse(settings));
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Failed to reset settings: {ex.Message}");
            return Task.FromResult(PromptEnhanceAPI.CreateErrorResponse(PromptEnhanceErrorCategory.Generic, $"Failed to reset settings: {ex.Message}"));
        }
    }
}
