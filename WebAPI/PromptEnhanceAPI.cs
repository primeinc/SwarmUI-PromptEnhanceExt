using System.Text.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using PromptEnhance.WebAPI.Models;

namespace PromptEnhance.WebAPI;

/// <summary>Permissions for PromptEnhance. Two concerns: making outbound calls to the configured backend,
/// and reading/writing the extension's own configuration.</summary>
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

/// <summary>API surface for the PromptEnhance extension. Registers exactly the routes the minimal contract needs and
/// provides the structured success/error payload helpers that every route returns. A thrown exception is never the
/// UI contract — routes catch and return <see cref="CreateErrorResponse(PromptEnhanceErrorCategory, string)"/>.</summary>
[API.APIClass("Prompt-enhancement routes for the PromptEnhance extension")]
public class PromptEnhanceAPI
{
    /// <summary>Registers the extension's API calls. Authorization is enforced solely by the <see cref="PermInfo"/>
    /// third argument (backend calls require <see cref="PromptEnhancePermissions.PermUseBackend"/>, config calls require
    /// <see cref="PromptEnhancePermissions.PermConfig"/>). The boolean is SwarmUI's <c>isUserUpdate</c> flag: when true
    /// it bumps the session's last-used time (idle-timeout bookkeeping), so per the built-in convention pure getters
    /// pass false and user-driven mutations/generation pass true.</summary>
    public static void Register()
    {
        API.RegisterAPICall(BackendClient.PromptEnhanceListModels, false, PromptEnhancePermissions.PermUseBackend);
        API.RegisterAPICall(BackendClient.PromptEnhanceRun, true, PromptEnhancePermissions.PermUseBackend);
        API.RegisterAPICall(SessionSettings.GetPromptEnhanceSettings, false, PromptEnhancePermissions.PermConfig);
        API.RegisterAPICall(SessionSettings.SavePromptEnhanceSettings, true, PromptEnhancePermissions.PermConfig);
        API.RegisterAPICall(SessionSettings.ResetPromptEnhanceSettings, true, PromptEnhancePermissions.PermConfig);
    }

    // ---- Structured response helpers (the UI contract) ---------------------------------------------------------

    /// <summary>Success payload carrying the enhanced prompt text.</summary>
    public static JObject CreateSuccessResponse(string response) => new()
    {
        ["success"] = true,
        ["response"] = response
    };

    /// <summary>Success payload carrying the list of available models. Each model is emitted as <c>{ id, name }</c>
    /// with lowercase keys — the shape <c>settings.js</c> reads (<c>m.id</c>/<c>m.name</c>) and the OpenAI convention.
    /// This is built explicitly rather than via <see cref="JArray.FromObject"/>, because that path uses Newtonsoft,
    /// which serializes <see cref="ModelData"/> by its C# property names (<c>Id</c>/<c>Name</c>) — ignoring the
    /// System.Text.Json attributes — and the model dropdown would then silently stay empty.</summary>
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

    /// <summary>Success payload carrying the current settings object.</summary>
    public static JObject CreateSettingsResponse(JObject settings) => new()
    {
        ["success"] = true,
        ["settings"] = settings
    };

    /// <summary>Structured error payload: a machine-readable category plus a formatted, actionable message.</summary>
    public static JObject CreateErrorResponse(PromptEnhanceErrorCategory category, string detail = null) => new()
    {
        ["success"] = false,
        ["errorCategory"] = ErrorHandler.CategoryCode(category),
        ["error"] = ErrorHandler.Format(category, detail)
    };

    // ---- Wire deserialization (System.Text.Json for the HTTP boundary) -----------------------------------------

    private static readonly JsonSerializerOptions WireOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Parses a <c>/v1/models</c> response into the UI model list. Returns null on an unparseable/unexpected
    /// shape so the caller can surface <see cref="PromptEnhanceErrorCategory.InvalidResponseShape"/>.</summary>
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

    /// <summary>Extracts the enhanced text from a <c>/v1/chat/completions</c> response
    /// (<c>choices[0].message.content</c>). Returns null on an unparseable/unexpected shape.</summary>
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

    /// <summary>Tries to pull a human-readable message out of an OpenAI-style error body. Returns the excerpted raw
    /// body when the error envelope is absent, so the caller always has something to show.</summary>
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
            // Not an OpenAI-style error envelope — fall through to the raw excerpt.
        }
        return ErrorHandler.Excerpt(json);
    }
}
