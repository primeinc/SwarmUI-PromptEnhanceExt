using System.Text.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using PromptEnhance.WebAPI.Models;

namespace PromptEnhance.WebAPI;

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

[API.APIClass("Prompt-enhancement routes for the PromptEnhance extension")]
public class PromptEnhanceAPI
{
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

    public static JObject CreateErrorResponse(PromptEnhanceErrorCategory category, string detail = null) => new()
    {
        ["success"] = false,
        ["errorCategory"] = ErrorHandler.CategoryCode(category),
        ["error"] = ErrorHandler.Format(category, detail)
    };

    private static readonly JsonSerializerOptions WireOptions = new() { PropertyNameCaseInsensitive = true };

    public static List<ModelData> DeserializeModels(string json)
    {
        try
        {
            ModelsListResponse parsed = JsonSerializer.Deserialize<ModelsListResponse>(json, WireOptions);
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

    public static string DeserializeChatContent(string json)
    {
        try
        {
            ChatCompletionResponse parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(json, WireOptions);
            string content = parsed?.Choices is { Count: > 0 } ? parsed.Choices[0].Message?.Content : null;
            return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        }
        catch (JsonException ex)
        {
            Logs.Error($"[PromptEnhance] Failed to parse chat response: {ex.Message}");
            return null;
        }
    }

    public static string ExtractErrorMessage(string json)
    {
        try
        {
            ChatErrorResponse parsed = JsonSerializer.Deserialize<ChatErrorResponse>(json, WireOptions);
            if (!string.IsNullOrWhiteSpace(parsed?.Error?.Message))
            {
                return parsed.Error.Message;
            }
        }
        catch (JsonException)
        {
        }
        return ErrorHandler.Excerpt(json);
    }
}
