using System.Net.Http;
using System.Text;
using System.Text.Json;
using Engine.Mcp;
using Xunit;

namespace Engine.Tests.Mcp;

public sealed class McpServerTests
{
    private static McpServer CreateServer(SceneBridge bridge, int port) => new();

    [Fact]
    public void InitializeReturnsProtocolVersionAndServerInfo()
    {
        SceneBridge bridge = new(Path.GetTempPath());
        using McpServer server = CreateServer(bridge, 0);
        (string response, int status, string? session) = server.HandleMessage(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""", null);
        Assert.Equal(200, status);
        Assert.NotNull(session);
        JsonElement result = JsonSerializer.Deserialize<JsonElement>(response);
        Assert.Equal("2025-06-18", result.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal("Engine", result.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public void ToolsListExposesEveryTool()
    {
        SceneBridge bridge = new(Path.GetTempPath());
        using McpServer server = CreateServer(bridge, 0);
        (string response, int status, string? _) = server.HandleMessage("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""", null);
        Assert.Equal(200, status);
        JsonElement result = JsonSerializer.Deserialize<JsonElement>(response);
        JsonElement tools = result.GetProperty("result").GetProperty("tools");
        Assert.Equal(McpTools.All.Count, tools.GetArrayLength());
        HashSet<string> names = tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!).ToHashSet();
        Assert.Contains("create_object", names);
        Assert.Contains("set_transform", names);
        Assert.Contains("screenshot", names);
    }

    [Fact]
    public void ToolCallReportsToolErrorsAsIsError()
    {
        SceneBridge bridge = new(Path.GetTempPath());
        using McpServer server = CreateServer(bridge, 0);
        (string response, int status, string? _) = server.HandleMessage(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_object","arguments":{"name":"Missing"}}}""", null);
        Assert.Equal(200, status);
        JsonElement result = JsonSerializer.Deserialize<JsonElement>(response);
        Assert.True(result.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("Error:", result.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void UnknownMethodReturnsMethodNotFound()
    {
        SceneBridge bridge = new(Path.GetTempPath());
        using McpServer server = CreateServer(bridge, 0);
        (string response, int status, string? _) = server.HandleMessage("""{"jsonrpc":"2.0","id":4,"method":"resources/list"}""", null);
        Assert.Equal(200, status);
        JsonElement result = JsonSerializer.Deserialize<JsonElement>(response);
        Assert.Equal(-32601, result.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void BrokenJsonReturnsParseError()
    {
        SceneBridge bridge = new(Path.GetTempPath());
        using McpServer server = CreateServer(bridge, 0);
        (string response, int status, string? _) = server.HandleMessage("{not json", null);
        Assert.Equal(400, status);
        Assert.Contains("-32700", response);
    }

    [Fact]
    public void NotificationReturnsAcceptedWithoutBody()
    {
        SceneBridge bridge = new(Path.GetTempPath());
        using McpServer server = CreateServer(bridge, 0);
        (string response, int status, string? _) = server.HandleMessage("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", null);
        Assert.Equal(202, status);
        Assert.Equal(string.Empty, response);
    }

    [Fact]
    public async Task HttpRoundTripServesInitializeAndTools()
    {
        SceneBridge bridge = new(Path.GetTempPath());
        using McpServer server = CreateServer(bridge, 0);
        int port = FreePort();
        server.Start(bridge, new McpServerOptions { Port = port, StatusPath = null });
        try
        {
            Assert.True(server.IsRunning);
            using HttpClient client = new();
            using HttpRequestMessage initialize = new(HttpMethod.Post, $"http://127.0.0.1:{port}/mcp")
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""", Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage initializeResponse = await client.SendAsync(initialize);
            initializeResponse.EnsureSuccessStatusCode();
            Assert.NotNull(initializeResponse.Headers.GetValues("Mcp-Session-Id").FirstOrDefault());
            string session = initializeResponse.Headers.GetValues("Mcp-Session-Id").First();
            using HttpRequestMessage call = new(HttpMethod.Post, $"http://127.0.0.1:{port}/mcp")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id = 2,
                        method = "tools/call",
                        @params = new { name = "create_object", arguments = new { name = "HttpCar" } },
                    }), Encoding.UTF8, "application/json"),
            };
            call.Headers.Add("Mcp-Session-Id", session);
            using HttpResponseMessage callResponse = await client.SendAsync(call);
            callResponse.EnsureSuccessStatusCode();
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(await callResponse.Content.ReadAsStringAsync());
            string text = result.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            Assert.Equal("HttpCar", JsonDocument.Parse(text).RootElement.GetProperty("name").GetString());
            Assert.Equal(1, server.ClientCount);
        }
        finally
        {
            server.Stop();
            Assert.False(server.IsRunning);
        }
    }

    [Fact]
    public void StartAndStopWritesStatusFile()
    {
        SceneBridge bridge = new(Path.GetTempPath());
        using McpServer server = CreateServer(bridge, 0);
        string status = Path.Combine(Path.GetTempPath(), $"mcp-status-{Guid.NewGuid():N}.json");
        int port = FreePort();
        server.Start(bridge, new McpServerOptions { Port = port, StatusPath = status });
        try
        {
            Assert.True(File.Exists(status));
            JsonElement statusJson = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(status));
            Assert.Equal(port, statusJson.GetProperty("port").GetInt32());
        }
        finally
        {
            server.Stop();
        }
        Assert.False(File.Exists(status));
    }

    private static int FreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
