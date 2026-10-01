using System.Net.Http;
using System.Reflection;

namespace PromptEnhance.Tests;

/// <summary>The extension builds its own HttpClient so it can turn redirects off. These tests fail when SwarmUI's NetworkBackendUtils.MakeHttpClient changes a client or handler setting the copy does not follow: every public settable property, one level into option objects such as SslOptions and CookieContainer, plus every default request header.</summary>
public class HttpClientParityTests
{
    /// <summary>The settings the extension deliberately differs on: no automatic redirects, a connect step that tries every resolved address at once, and per-request timeouts instead of a client-wide one.</summary>
    private static readonly string[] DeliberateDifferences = [nameof(SocketsHttpHandler.AllowAutoRedirect), nameof(SocketsHttpHandler.ConnectCallback), nameof(HttpClient.Timeout)];

    private static SocketsHttpHandler HandlerOf(HttpClient client)
    {
        FieldInfo field = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("HttpMessageInvoker._handler not found; this runtime changed how HttpClient stores its handler.");
        return field.GetValue(client) as SocketsHttpHandler
            ?? throw new InvalidOperationException("The client's handler is not a SocketsHttpHandler.");
    }

    private static (object? Value, string? Error) Read(PropertyInfo property, object owner)
    {
        try
        {
            return (property.GetValue(owner), null);
        }
        catch (TargetInvocationException ex)
        {
            return (null, ex.InnerException?.GetType().Name ?? ex.GetType().Name);
        }
    }

    /// <summary>Compares every public read/write property of <paramref name="type"/> on both objects. Values and types that override Equals (string, Version, Uri, delegates) compare by Equals, other objects by runtime type and then, while <paramref name="depth"/> allows, by their own properties.</summary>
    private static int Compare(Type type, object expected, object actual, string path, int depth, List<string> drift)
    {
        int compared = 0;
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0 || DeliberateDifferences.Contains(property.Name))
            {
                continue;
            }
            compared++;
            string name = $"{path}.{property.Name}";
            (object? want, string? wantError) = Read(property, expected);
            (object? got, string? gotError) = Read(property, actual);
            if (wantError is not null || gotError is not null)
            {
                if (wantError != gotError)
                {
                    drift.Add($"{name}: SwarmUI {wantError ?? want}, extension {gotError ?? got}");
                }
                continue;
            }
            if (want is null || got is null)
            {
                if (want is not null || got is not null)
                {
                    drift.Add($"{name}: SwarmUI {want?.ToString() ?? "null"}, extension {got?.ToString() ?? "null"}");
                }
                continue;
            }
            Type valueType = property.PropertyType;
            bool comparesByValue = valueType.IsValueType || want.GetType().GetMethod(nameof(Equals), [typeof(object)])!.DeclaringType != typeof(object);
            if (comparesByValue)
            {
                if (!Equals(want, got))
                {
                    drift.Add($"{name}: SwarmUI {want}, extension {got}");
                }
            }
            else if (want.GetType() != got.GetType())
            {
                drift.Add($"{name}: SwarmUI {want.GetType()}, extension {got.GetType()}");
            }
            else if (depth > 0)
            {
                compared += Compare(want.GetType(), want, got, name, depth - 1, drift);
            }
        }
        return compared;
    }

    [Xunit.Fact]
    public void ExtensionClient_MatchesSwarmUIsMakeHttpClient_ExceptRedirectsAndTimeout()
    {
        using HttpClient host = SwarmUI.Backends.NetworkBackendUtils.MakeHttpClient();
        HttpClient ours = WebAPI.BackendClient.HttpClient;
        List<string> drift = [];
        int compared = Compare(typeof(HttpClient), host, ours, "HttpClient", 0, drift)
            + Compare(typeof(SocketsHttpHandler), HandlerOf(host), HandlerOf(ours), "SocketsHttpHandler", 1, drift);
        Xunit.Assert.True(compared > 30, $"control: only {compared} settings compared");
        Xunit.Assert.Empty(drift);
        Xunit.Assert.Equal(host.DefaultRequestHeaders.ToString(), ours.DefaultRequestHeaders.ToString());
    }

    [Xunit.Fact]
    public void ExtensionClient_DoesNotFollowRedirects_ConnectsToAllAddresses_AndLeavesTimeoutsToEachRequest()
    {
        Xunit.Assert.False(HandlerOf(WebAPI.BackendClient.HttpClient).AllowAutoRedirect);
        Xunit.Assert.NotNull(HandlerOf(WebAPI.BackendClient.HttpClient).ConnectCallback);
        Xunit.Assert.Equal(Timeout.InfiniteTimeSpan, WebAPI.BackendClient.HttpClient.Timeout);
    }
}
