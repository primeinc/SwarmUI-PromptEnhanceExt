using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

public class BackendTransportTests
{
    private const string ModelsBody = "{\"object\":\"list\",\"data\":[{\"id\":\"mock-enhancer\",\"object\":\"model\"}]}";
    private const string ChatBody = "{\"id\":\"x\",\"model\":\"mock-enhancer\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"an enhanced prompt\"}}]}";

    [Xunit.Fact]
    public async Task ExecuteListModels_Success_ParsesModelList()
    {
        using MockHttpServer server = new(200, "OK", ModelsBody);
        JObject r = await WebAPI.BackendClient.ExecuteListModels(server.BaseUrl, 30);
        Xunit.Assert.True(r["success"]!.Value<bool>());
        JArray models = (JArray)r["models"]!;
        Xunit.Assert.Single(models);
        Xunit.Assert.Equal("mock-enhancer", ((JObject)models[0])["id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteListModels_404_ClassifiesModelMissing()
    {
        using MockHttpServer server = new(404, "Not Found", "{\"error\":{\"message\":\"no such route\"}}");
        JObject r = await WebAPI.BackendClient.ExecuteListModels(server.BaseUrl, 30);
        Xunit.Assert.False(r["success"]!.Value<bool>());
        Xunit.Assert.Equal("model_missing", r["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteListModels_500_ClassifiesServerUnavailable()
    {
        using MockHttpServer server = new(500, "Internal Server Error", "boom");
        JObject r = await WebAPI.BackendClient.ExecuteListModels(server.BaseUrl, 30);
        Xunit.Assert.Equal("server_unavailable", r["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteListModels_MalformedJson_ClassifiesInvalidResponseShape()
    {
        using MockHttpServer server = new(200, "OK", "this is not json");
        JObject r = await WebAPI.BackendClient.ExecuteListModels(server.BaseUrl, 30);
        Xunit.Assert.Equal("invalid_response_shape", r["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_Success_ReturnsExtractedContent()
    {
        using MockHttpServer server = new(200, "OK", ChatBody);
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "mock-enhancer", "sys", "a cat", [], 0.7, 1024, 30);
        Xunit.Assert.True(r["success"]!.Value<bool>());
        Xunit.Assert.Equal("an enhanced prompt", r["response"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_401_ClassifiesAuthentication()
    {
        using MockHttpServer server = new(401, "Unauthorized", "{\"error\":{\"message\":\"missing key\"}}");
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "hi", [], 0.7, 1024, 30);
        Xunit.Assert.Equal("authentication", r["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_MalformedJson_ClassifiesInvalidResponseShape()
    {
        using MockHttpServer server = new(200, "OK", "{ not valid json");
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "hi", [], 0.7, 1024, 30);
        Xunit.Assert.Equal("invalid_response_shape", r["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_ImageBlaming400WithMedia_ClassifiesUnsupportedImage()
    {
        using MockHttpServer server = new(400, "Bad Request", "{\"error\":{\"message\":\"this model does not support image input\"}}");
        List<BackendSchema.MediaContent> media = [new() { Type = "base64", Data = "QUJD", MediaType = "image/png" }];
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "describe", media, 0.7, 1024, 30);
        Xunit.Assert.Equal("unsupported_image", r["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_Bare400WithMedia_ClassifiesHttpError_NotImage()
    {
        using MockHttpServer server = new(400, "Bad Request", "{\"error\":{\"message\":\"maximum context length exceeded\"}}");
        List<BackendSchema.MediaContent> media = [new() { Type = "base64", Data = "QUJD", MediaType = "image/png" }];
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "describe", media, 0.7, 1024, 30);
        Xunit.Assert.Equal("http_error", r["error_id"]!.Value<string>());
    }

    [Xunit.Theory]
    [Xunit.InlineData(301, "Moved Permanently")]
    [Xunit.InlineData(302, "Found")]
    [Xunit.InlineData(307, "Temporary Redirect")]
    [Xunit.InlineData(308, "Permanent Redirect")]
    public async Task Redirects_AreNotFollowed_AndReportedWithTheirTarget(int status, string reason)
    {
        using MockHttpServer elsewhere = new(200, "OK", ChatBody);
        using MockHttpServer server = new(status, reason, "", location: $"{elsewhere.BaseUrl}/v1/chat/completions");
        JObject chat = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "secret prompt", [], 0.7, 1024, 30);
        JObject models = await WebAPI.BackendClient.ExecuteListModels(server.BaseUrl, 30);
        Xunit.Assert.Empty(elsewhere.RequestHeads);
        Xunit.Assert.Equal(2, server.RequestHeads.Count);
        foreach (JObject r in new[] { chat, models })
        {
            Xunit.Assert.False(r["success"]!.Value<bool>());
            Xunit.Assert.Equal("http_error", r["error_id"]!.Value<string>());
            Xunit.Assert.Contains("redirect", r["error"]!.Value<string>(), StringComparison.OrdinalIgnoreCase);
            Xunit.Assert.Contains(elsewhere.BaseUrl, r["error"]!.Value<string>());
        }
    }

    [Xunit.Fact]
    public async Task ExecuteChat_Timeout_ClassifiesTimeout()
    {
        using MockHttpServer server = new(200, "OK", ChatBody, delayMs: 3000);
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "hi", [], 0.7, 1024, 1);
        Xunit.Assert.Equal("timeout", r["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_ConnectionRefused_ClassifiesServerUnavailable()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        JObject r = await WebAPI.BackendClient.ExecuteChat($"http://127.0.0.1:{deadPort}", "m", "sys", "hi", [], 0.7, 1024, 5);
        Xunit.Assert.Equal("server_unavailable", r["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task PromptEnhanceListModels_DeadBackend_ClassifiesServerUnavailable()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        SwarmUI.Accounts.Session session = TestSessions.MakeRealSession();
        session.User.SaveGenericData("promptenhance", "config", $"{{\"baseUrl\":\"http://127.0.0.1:{deadPort}\"}}");
        JObject r = await WebAPI.BackendClient.PromptEnhanceListModels(session);
        Xunit.Assert.False(r["success"]!.Value<bool>());
        Xunit.Assert.Equal("server_unavailable", r["error_id"]!.Value<string>());
    }

    /// <summary>localhost resolves to ::1 and 127.0.0.1. Windows retries a refused SYN for about 2s per address, so trying them one after the other outlasts the 3s probe, whose timeout counts as reachable, and every call pays the full wait again.</summary>
    [Xunit.Fact]
    public async Task PromptEnhanceListModels_DeadLocalhost_IsCachedUnreachable()
    {
        using Socket reserved = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reserved.LocalEndPoint!).Port;
        SwarmUI.Accounts.Session session = TestSessions.MakeRealSession();
        session.User.SaveGenericData("promptenhance", "config", $"{{\"baseUrl\":\"http://localhost:{port}\"}}");
        JObject first = await WebAPI.BackendClient.PromptEnhanceListModels(session);
        Xunit.Assert.Equal("server_unavailable", first["error_id"]!.Value<string>());
        Stopwatch again = Stopwatch.StartNew();
        JObject second = await WebAPI.BackendClient.PromptEnhanceListModels(session);
        again.Stop();
        Xunit.Assert.Equal("server_unavailable", second["error_id"]!.Value<string>());
        Xunit.Assert.True(again.ElapsedMilliseconds < 500, $"second call took {again.ElapsedMilliseconds} ms; the probe did not cache the dead backend");
    }

    /// <summary>A backend listening on 127.0.0.1 only, reached as localhost: the ::1 attempt is refused while the IPv4 one connects, and the request goes through without waiting out the refusal.</summary>
    [Xunit.Fact]
    public async Task ExecuteListModels_LocalhostWithIPv4OnlyBackend_ConnectsWithoutWaitingOnRefusedAddress()
    {
        using MockHttpServer server = new(200, "OK", ModelsBody);
        Stopwatch elapsed = Stopwatch.StartNew();
        JObject r = await WebAPI.BackendClient.ExecuteListModels($"http://localhost:{server.Port}", 30);
        elapsed.Stop();
        Xunit.Assert.True(r["success"]!.Value<bool>(), r.ToString());
        Xunit.Assert.Single(server.RequestHeads);
        Xunit.Assert.True(elapsed.ElapsedMilliseconds < 1000, $"took {elapsed.ElapsedMilliseconds} ms; the connect waited on a refused address");
    }

    [Xunit.Fact]
    public async Task PromptEnhanceListModels_BackendOnAPortThatWasDeadMomentsAgo_IsReachable()
    {
        // Bound but not listening: connections are refused, and no other process can take the port before the server does.
        using Socket reserved = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reserved.LocalEndPoint!).Port;
        SwarmUI.Accounts.Session session = TestSessions.MakeRealSession();
        session.User.SaveGenericData("promptenhance", "config", $"{{\"baseUrl\":\"http://127.0.0.1:{port}\"}}");
        JObject dead = await WebAPI.BackendClient.PromptEnhanceListModels(session);
        Xunit.Assert.Equal("server_unavailable", dead["error_id"]!.Value<string>());
        reserved.Dispose();
        using MockHttpServer live = new(200, "OK", ModelsBody, port: port);
        JObject r = await WebAPI.BackendClient.PromptEnhanceListModels(session);
        Xunit.Assert.True(r["success"]!.Value<bool>(), r.ToString());
    }

    [Xunit.Fact]
    public async Task PromptEnhanceListModels_LiveBackend_PassesProbeAndReturnsModels()
    {
        using MockHttpServer server = new(200, "OK", ModelsBody);
        SwarmUI.Accounts.Session session = TestSessions.MakeRealSession();
        session.User.SaveGenericData("promptenhance", "config", $"{{\"baseUrl\":\"{server.BaseUrl}\"}}");
        JObject r = await WebAPI.BackendClient.PromptEnhanceListModels(session);
        Xunit.Assert.True(r["success"]!.Value<bool>());
        JArray models = (JArray)r["models"]!;
        Xunit.Assert.Single(models);
        Xunit.Assert.Equal("mock-enhancer", ((JObject)models[0])["id"]!.Value<string>());
    }
}

internal sealed class MockHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly int _status;
    private readonly string _reason;
    private readonly string _body;
    private readonly int _delayMs;
    private readonly string? _location;
    private volatile bool _stop;

    /// <summary>The header block (request line plus headers) of every request received, in arrival order.</summary>
    public readonly ConcurrentQueue<string> RequestHeads = new();

    /// <summary>Listens on <paramref name="port"/> (0 picks a free one). A backend now answering at this URL makes any cached "unreachable" probe result for it stale, so it is forgotten.</summary>
    public MockHttpServer(int status, string reason, string body, int delayMs = 0, string? location = null, int port = 0)
    {
        _status = status;
        _reason = reason;
        _body = body;
        _delayMs = delayMs;
        _location = location;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        WebAPI.BackendClient.ForgetReachability(BaseUrl);
        _ = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    private async Task AcceptLoopAsync()
    {
        while (!_stop)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch
            {
                return;
            }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                stream.ReadTimeout = 2000;
                byte[] buf = new byte[8192];
                using MemoryStream received = new();
                try
                {
                    while (true)
                    {
                        int n = await stream.ReadAsync(buf);
                        if (n <= 0)
                        {
                            break;
                        }
                        received.Write(buf, 0, n);
                        string soFar = Encoding.ASCII.GetString(received.ToArray());
                        int headerEnd = soFar.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (headerEnd >= 0)
                        {
                            int contentLength = ParseContentLength(soFar);
                            long bodyHave = received.Length - (headerEnd + 4);
                            if (bodyHave >= contentLength)
                            {
                                break;
                            }
                        }
                    }
                }
                catch
                {
                }
                string raw = Encoding.ASCII.GetString(received.ToArray());
                int end = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                RequestHeads.Enqueue(end >= 0 ? raw[..end] : raw);

                if (_delayMs > 0)
                {
                    await Task.Delay(_delayMs);
                }

                byte[] bodyBytes = Encoding.UTF8.GetBytes(_body ?? "");
                StringBuilder head = new();
                head.Append($"HTTP/1.1 {_status} {_reason}\r\n");
                head.Append("Content-Type: application/json\r\n");
                if (_location != null)
                {
                    head.Append($"Location: {_location}\r\n");
                }
                head.Append($"Content-Length: {bodyBytes.Length}\r\n");
                head.Append("Connection: close\r\n\r\n");
                byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
                await stream.WriteAsync(headBytes);
                await stream.WriteAsync(bodyBytes);
                await stream.FlushAsync();
            }
        }
        catch
        {
        }
    }

    private static int ParseContentLength(string headers)
    {
        foreach (string line in headers.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line["Content-Length:".Length..].Trim(), out int v))
            {
                return v;
            }
        }
        return 0;
    }

    public void Dispose()
    {
        _stop = true;
        try
        {
            _listener.Stop();
        }
        catch
        {
        }
    }
}
