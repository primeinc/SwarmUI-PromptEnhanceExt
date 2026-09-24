using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Html;
using SwarmUI.Accounts;
using SwarmUI.WebAPI;

namespace PromptEnhance.WebAPI;

/// <summary>The per-user API key for the configured backend. Users set it in SwarmUI's User → API Keys table; it is stored in the user's generic data and never returned to the browser.</summary>
public static class UpstreamApiKey
{
    /// <summary>Key type in SwarmUI's API key registry and generic-data store. Mirrors `apiKeyType` in contracts/pe-contract.json.</summary>
    public const string KeyType = "promptenhance_api";

    /// <summary>Adds the key to SwarmUI's User → API Keys table and to the key types the host `SetAPIKey` route accepts. Safe to call more than once.</summary>
    public static void Register()
    {
        BasicAPIFeatures.AcceptedAPIKeyTypes.Add(KeyType);
        if (!UserUpstreamApiKeys.KeysByType.ContainsKey(KeyType))
        {
            UserUpstreamApiKeys.Register(new(KeyType, "promptenhance", "PromptEnhance LLM Server",
                "https://github.com/primeinc/SwarmUI-PromptEnhanceExt#api-keys",
                new HtmlString("The API key PromptEnhance sends, as <code>Authorization: Bearer</code>, to the server at its Base URL. Leave it unset for servers that need no key, such as a local Ollama, LM Studio, or llama.cpp.")));
        }
    }

    /// <summary>The session user's key with surrounding whitespace removed, or null when none is set.</summary>
    public static string ForUser(Session session)
    {
        string key = session?.User?.GetGenericData(KeyType, "key")?.Trim();
        return string.IsNullOrEmpty(key) ? null : key;
    }

    /// <summary>True when the key can travel as a bearer token: printable ASCII only (0x21-0x7E), since .NET refuses non-ASCII header values and whitespace or control characters would split the header.</summary>
    public static bool IsSendable(string key) => key.All(c => c >= '!' && c <= '~');

    /// <summary>Sets `Authorization: Bearer` when a key is present. Callers check <see cref="IsSendable"/> first.</summary>
    public static void Apply(HttpRequestMessage request, string key)
    {
        if (key != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
    }
}
