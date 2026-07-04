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
        Xunit.Assert.Equal("model_missing", r["errorCategory"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteListModels_500_ClassifiesServerUnavailable()
    {
        using MockHttpServer server = new(500, "Internal Server Error", "boom");
        JObject r = await WebAPI.BackendClient.ExecuteListModels(server.BaseUrl, 30);
        Xunit.Assert.Equal("server_unavailable", r["errorCategory"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteListModels_MalformedJson_ClassifiesInvalidResponseShape()
    {
        using MockHttpServer server = new(200, "OK", "this is not json");
        JObject r = await WebAPI.BackendClient.ExecuteListModels(server.BaseUrl, 30);
        Xunit.Assert.Equal("invalid_response_shape", r["errorCategory"]!.Value<string>());
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
        Xunit.Assert.Equal("authentication", r["errorCategory"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_MalformedJson_ClassifiesInvalidResponseShape()
    {
        using MockHttpServer server = new(200, "OK", "{ not valid json");
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "hi", [], 0.7, 1024, 30);
        Xunit.Assert.Equal("invalid_response_shape", r["errorCategory"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_ImageBlaming400WithMedia_ClassifiesUnsupportedImage()
    {
        using MockHttpServer server = new(400, "Bad Request", "{\"error\":{\"message\":\"this model does not support image input\"}}");
        List<BackendSchema.MediaContent> media = [new() { Type = "base64", Data = "QUJD", MediaType = "image/png" }];
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "describe", media, 0.7, 1024, 30);
        Xunit.Assert.Equal("unsupported_image", r["errorCategory"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_Bare400WithMedia_ClassifiesHttpError_NotImage()
    {
        using MockHttpServer server = new(400, "Bad Request", "{\"error\":{\"message\":\"maximum context length exceeded\"}}");
        List<BackendSchema.MediaContent> media = [new() { Type = "base64", Data = "QUJD", MediaType = "image/png" }];
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "describe", media, 0.7, 1024, 30);
        Xunit.Assert.Equal("http_error", r["errorCategory"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_Timeout_ClassifiesTimeout()
    {
        using MockHttpServer server = new(200, "OK", ChatBody, delayMs: 3000);
        JObject r = await WebAPI.BackendClient.ExecuteChat(server.BaseUrl, "m", "sys", "hi", [], 0.7, 1024, 1);
        Xunit.Assert.Equal("timeout", r["errorCategory"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task ExecuteChat_ConnectionRefused_ClassifiesServerUnavailable()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        JObject r = await WebAPI.BackendClient.ExecuteChat($"http://127.0.0.1:{deadPort}", "m", "sys", "hi", [], 0.7, 1024, 5);
        Xunit.Assert.Equal("server_unavailable", r["errorCategory"]!.Value<string>());
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
        Xunit.Assert.Equal("server_unavailable", r["errorCategory"]!.Value<string>());
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
    private volatile bool _stop;

    public MockHttpServer(int status, string reason, string body, int delayMs = 0)
    {
        _status = status;
        _reason = reason;
        _body = body;
        _delayMs = delayMs;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
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

                if (_delayMs > 0)
                {
                    await Task.Delay(_delayMs);
                }

                byte[] bodyBytes = Encoding.UTF8.GetBytes(_body ?? "");
                StringBuilder head = new();
                head.Append($"HTTP/1.1 {_status} {_reason}\r\n");
                head.Append("Content-Type: application/json\r\n");
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
