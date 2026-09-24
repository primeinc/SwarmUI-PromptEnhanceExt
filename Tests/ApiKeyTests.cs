using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.WebAPI;

namespace PromptEnhance.Tests;

/// <summary>The per-user backend API key: host registration, the Authorization header on the wire, and that the key never comes back out.</summary>
public class ApiKeyTests
{
    private const string ModelsBody = "{\"object\":\"list\",\"data\":[{\"id\":\"mock-enhancer\",\"object\":\"model\"}]}";
    private const string ChatBody = "{\"id\":\"x\",\"model\":\"mock-enhancer\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"an enhanced prompt\"}}]}";

    private static string[] AuthorizationLines(MockHttpServer server) =>
        [.. server.RequestHeads.SelectMany(head => head.Split("\r\n")).Where(line => line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))];

    private static Session SessionFor(MockHttpServer server, string? key)
    {
        Session session = TestSessions.MakeRealSession();
        session.User.SaveGenericData("promptenhance", "config", $"{{\"baseUrl\":\"{server.BaseUrl}\",\"model\":\"mock-enhancer\"}}");
        if (key != null)
        {
            session.User.SaveGenericData(WebAPI.UpstreamApiKey.KeyType, "key", key);
        }
        return session;
    }

    [Xunit.Fact]
    public void KeyType_MatchesContract()
    {
        Xunit.Assert.Equal(WebAPI.UpstreamApiKey.KeyType, ContractParityTests.Contract()["apiKeyType"]!.Value<string>());
    }

    [Xunit.Fact]
    public void Register_AddsTheKeyToSwarmUIsApiKeyTableAndSetAPIKey_Idempotently()
    {
        WebAPI.UpstreamApiKey.Register();
        WebAPI.UpstreamApiKey.Register();
        Xunit.Assert.True(UserUpstreamApiKeys.KeysByType.TryGetValue(WebAPI.UpstreamApiKey.KeyType, out UserUpstreamApiKeys.ApiKeyInfo? info));
        Xunit.Assert.Equal("promptenhance", info!.JSPrefix);
        Xunit.Assert.Contains(WebAPI.UpstreamApiKey.KeyType, BasicAPIFeatures.AcceptedAPIKeyTypes);
    }

    [Xunit.Fact]
    public async Task ExecuteListModels_WithKey_SendsBearerAuthorization()
    {
        using MockHttpServer server = new(200, "OK", ModelsBody);
        JObject r = await WebAPI.BackendClient.ExecuteListModels(server.BaseUrl, 30, "sk-models");
        Xunit.Assert.True(r["success"]!.Value<bool>());
        Xunit.Assert.Equal(["Authorization: Bearer sk-models"], AuthorizationLines(server));
    }

    [Xunit.Fact]
    public async Task ExecuteChat_WithKey_SendsBearerAuthorization()
    {
        using MockHttpServer server = new(200, "OK", ChatBody);
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "hi", [], 0.7, 1024, 30, "sk-chat");
        Xunit.Assert.True(r["success"]!.Value<bool>());
        Xunit.Assert.Equal(["Authorization: Bearer sk-chat"], AuthorizationLines(server));
    }

    [Xunit.Fact]
    public async Task WithoutKey_NoAuthorizationHeaderIsSent()
    {
        using MockHttpServer server = new(200, "OK", ChatBody);
        await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "hi", [], 0.7, 1024, 30);
        await WebAPI.BackendClient.ExecuteListModels(server.BaseUrl, 30);
        Xunit.Assert.Equal(2, server.RequestHeads.Count);
        Xunit.Assert.Empty(AuthorizationLines(server));
    }

    [Xunit.Fact]
    public async Task PromptEnhanceRun_SendsTheSessionUsersTrimmedKey_OnTheChatCallOnly()
    {
        using MockHttpServer server = new(200, "OK", ChatBody);
        JObject r = await WebAPI.BackendClient.PromptEnhanceRun(new JObject { ["prompt"] = "a cat" }, SessionFor(server, "  sk-user  "));
        Xunit.Assert.True(r["success"]!.Value<bool>(), r.ToString());
        string chatHead = server.RequestHeads.Single(head => head.StartsWith("POST /v1/chat/completions", StringComparison.Ordinal));
        Xunit.Assert.Contains("Authorization: Bearer sk-user\r\n", chatHead + "\r\n");
        string probeHead = server.RequestHeads.Single(head => head.StartsWith("GET /v1/models", StringComparison.Ordinal));
        Xunit.Assert.DoesNotContain("Authorization:", probeHead, StringComparison.OrdinalIgnoreCase);
    }

    [Xunit.Fact]
    public async Task PromptEnhanceListModels_SendsTheSessionUsersKey()
    {
        using MockHttpServer server = new(200, "OK", ModelsBody);
        JObject r = await WebAPI.BackendClient.PromptEnhanceListModels(SessionFor(server, "sk-list"));
        Xunit.Assert.True(r["success"]!.Value<bool>(), r.ToString());
        Xunit.Assert.Equal(["Authorization: Bearer sk-list"], AuthorizationLines(server));
    }

    [Xunit.Fact]
    public async Task KeyThatCannotBeAHeaderValue_IsRejectedWithoutSendingOrEchoingIt()
    {
        using MockHttpServer server = new(200, "OK", ChatBody);
        JObject r = await WebAPI.BackendClient.PromptEnhanceRun(new JObject { ["prompt"] = "a cat" }, SessionFor(server, "sk-bad\nInjected: yes"));
        Xunit.Assert.False(r["success"]!.Value<bool>());
        Xunit.Assert.Equal("authentication", r["error_id"]!.Value<string>());
        Xunit.Assert.DoesNotContain("sk-bad", r.ToString());
        Xunit.Assert.DoesNotContain(server.RequestHeads, head => head.StartsWith("POST", StringComparison.Ordinal));
    }

    [Xunit.Fact]
    public async Task GetPromptEnhanceSettings_NeverReturnsTheKey()
    {
        using MockHttpServer server = new(200, "OK", ModelsBody);
        JObject r = await WebAPI.SessionSettings.GetPromptEnhanceSettings(SessionFor(server, "sk-secret-value"));
        Xunit.Assert.True(r["success"]!.Value<bool>());
        Xunit.Assert.DoesNotContain("sk-secret-value", r.ToString());
    }

    [Xunit.Fact]
    public async Task AuthenticationError_PointsAtTheApiKeySetting()
    {
        using MockHttpServer server = new(401, "Unauthorized", "{\"error\":{\"message\":\"missing key\"}}");
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "hi", [], 0.7, 1024, 30);
        Xunit.Assert.Equal("authentication", r["error_id"]!.Value<string>());
        Xunit.Assert.Contains("User → API Keys", r["error"]!.Value<string>());
    }
}
