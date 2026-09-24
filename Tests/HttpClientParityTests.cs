using System.Net.Http;
using System.Reflection;

namespace PromptEnhance.Tests;

/// <summary>The extension builds its own HttpClient so it can turn redirects off. These tests fail when SwarmUI's NetworkBackendUtils.MakeHttpClient changes a setting the copy does not follow.</summary>
public class HttpClientParityTests
{
    /// <summary>The settings the extension deliberately differs on: no automatic redirects, and per-request timeouts instead of a client-wide one.</summary>
    private static readonly string[] DeliberateDifferences = [nameof(SocketsHttpHandler.AllowAutoRedirect)];

    private static SocketsHttpHandler HandlerOf(HttpClient client)
    {
        FieldInfo field = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("HttpMessageInvoker._handler not found; this runtime changed how HttpClient stores its handler.");
        return field.GetValue(client) as SocketsHttpHandler
            ?? throw new InvalidOperationException("The client's handler is not a SocketsHttpHandler.");
    }

    [Xunit.Fact]
    public void ExtensionClient_MatchesSwarmUIsMakeHttpClient_ExceptRedirects()
    {
        using HttpClient host = SwarmUI.Backends.NetworkBackendUtils.MakeHttpClient();
        SocketsHttpHandler hostHandler = HandlerOf(host);
        SocketsHttpHandler ours = HandlerOf(WebAPI.BackendClient.HttpClient);
        List<string> drift = [];
        int compared = 0;
        foreach (PropertyInfo property in typeof(SocketsHttpHandler).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || DeliberateDifferences.Contains(property.Name))
            {
                continue;
            }
            Type type = property.PropertyType;
            if (!type.IsValueType && type != typeof(string))
            {
                continue;
            }
            compared++;
            object? expected = property.GetValue(hostHandler);
            object? actual = property.GetValue(ours);
            if (!Equals(expected, actual))
            {
                drift.Add($"{property.Name}: SwarmUI {expected}, extension {actual}");
            }
        }
        Xunit.Assert.True(compared > 10, $"control: only {compared} handler settings compared");
        Xunit.Assert.Empty(drift);
        Xunit.Assert.Equal(host.DefaultRequestHeaders.UserAgent.ToString(), WebAPI.BackendClient.HttpClient.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Xunit.Fact]
    public void ExtensionClient_DoesNotFollowRedirects_AndLeavesTimeoutsToEachRequest()
    {
        Xunit.Assert.False(HandlerOf(WebAPI.BackendClient.HttpClient).AllowAutoRedirect);
        Xunit.Assert.Equal(Timeout.InfiniteTimeSpan, WebAPI.BackendClient.HttpClient.Timeout);
    }
}
