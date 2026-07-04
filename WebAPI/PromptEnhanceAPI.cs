using System.Text.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using PromptEnhance.WebAPI.Models;

namespace PromptEnhance.WebAPI;

/// <summary>The extension's two permission nodes. Both default to POWERUSERS with the POWERFUL safety level.</summary>
public static class PromptEnhancePermissions
{
    public static readonly PermInfoGroup PromptEnhancePermGroup =
        new("PromptEnhance", "Permissions for the PromptEnhance prompt-enhancement extension.");

    public static readonly PermInfo PermUseBackend = Permissions.Register(new("promptenhance_use_backend",
        "Use Backend", "Allows outbound calls to the configured OpenAI-compatible backend (list models, enhance prompt).",
        PermissionDefault.POWERUSERS, PromptEnhancePermGroup, PermSafetyLevel.POWERFUL));

    public static readonly PermInfo PermConfig = Permissions.Register(new("promptenhance_config",
        "Manage Configuration", "Allows reading, saving, and resetting PromptEnhance settings.",
        PermissionDefault.POWERUSERS, PromptEnhancePermGroup, PermSafetyLevel.POWERFUL));
}

/// <summary>Route registration plus the response-envelope and wire-deserialization helpers shared by the API surface.</summary>
[API.APIClass("Prompt-enhancement routes for the PromptEnhance extension")]
public class PromptEnhanceAPI
{
    /// <summary>Registers the five routes. The bool is SwarmUI's IsUserUpdate flag: true for run/save/reset, false for list models and get settings.</summary>
    public static void Register()
    {
        API.RegisterAPICall(BackendClient.PromptEnhanceListModels, false, PromptEnhancePermissions.PermUseBackend);
        API.RegisterAPICall(BackendClient.PromptEnhanceRun, true, PromptEnhancePermissions.PermUseBackend);
        API.RegisterAPICall(SessionSettings.GetPromptEnhanceSettings, false, PromptEnhancePermissions.PermConfig);
        API.RegisterAPICall(SessionSettings.SavePromptEnhanceSettings, true, PromptEnhancePermissions.PermConfig);
        API.RegisterAPICall(SessionSettings.ResetPromptEnhanceSettings, true, PromptEnhancePermissions.PermConfig);
    }

    public static JObject CreateSuccessResponse(string response) => new()
    {
        ["success"] = true,
        ["response"] = response
    };

    public static JObject CreateModelsResponse(List<ModelData> models)
    {
        JArray array = [];
        foreach (ModelData model in models ?? [])
        {
            array.Add(new JObject { ["id"] = model.Id, ["name"] = model.Name });
        }
        return new JObject
        {
            ["success"] = true,
            ["models"] = array
        };
    }

    public static JObject CreateSettingsResponse(JObject settings) => new()
    {
        ["success"] = true,
        ["settings"] = settings
    };

    /// <summary>The single error envelope: success=false plus the stable errorCategory code and error text.</summary>
    public static JObject CreateErrorResponse(PromptEnhanceErrorCategory category, string detail = null) => new()
    {
        ["success"] = false,
        ["errorCategory"] = ErrorHandler.CategoryCode(category),
        ["error"] = ErrorHandler.Format(category, detail)
    };

    /// <summary>Adapter: `/v1/models` body -> model list. Returns null when the body is not the expected envelope; entries without an id are dropped.</summary>
    public static List<ModelData> DeserializeModels(string json)
    {
        try
        {
            ModelsListResponse parsed = JsonSerializer.Deserialize<ModelsListResponse>(json, ErrorHandler.WireOptions);
            if (parsed?.Data == null)
            {
                return null;
            }
            return [.. parsed.Data
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => new ModelData { Id = m.Id, Name = m.Id })];
        }
        catch (JsonException ex)
        {
            Logs.Error($"[PromptEnhance] Failed to parse models response: {ex.Message}");
            return null;
        }
    }

    /// <summary>Adapter: chat-completion body -> the first choice's trimmed message content. Returns null for malformed JSON, no choices, or blank content.</summary>
    public static string DeserializeChatContent(string json)
    {
        try
        {
            ChatCompletionResponse parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(json, ErrorHandler.WireOptions);
            string content = parsed?.Choices is { Count: > 0 } ? parsed.Choices[0].Message?.Content : null;
            return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        }
        catch (JsonException ex)
        {
            Logs.Error($"[PromptEnhance] Failed to parse chat response: {ex.Message}");
            return null;
        }
    }

    /// <summary>Pulls the message out of an OpenAI-style error envelope; when the body isn't that shape, the raw excerpt is returned.</summary>
    public static string ExtractErrorMessage(string json)
    {
        ChatError error = ErrorHandler.TryParseErrorEnvelope(json);
        return string.IsNullOrWhiteSpace(error?.Message) ? ErrorHandler.Excerpt(json) : error.Message;
    }
}
