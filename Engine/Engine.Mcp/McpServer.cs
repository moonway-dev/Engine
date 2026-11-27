using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Engine.Mcp;

public sealed class McpServerOptions
{
    public int Port { get; set; } = 8777;
    public string ServerName { get; set; } = "Engine";
    public string Version { get; set; } = "1.0.0";
    public string? StatusPath { get; set; }
}

public sealed class McpServer : IDisposable
{
    private const string ProtocolVersion = "2025-06-18";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = [];
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private IMcpEngineBridge? _bridge;
    private McpServerOptions _options = new();

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    public string ServerName => _options.ServerName;
    public int ClientCount
    {
        get
        {
            lock (_stateLock)
            {
                DateTimeOffset cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(90);
                foreach ((string id, DateTimeOffset seen) in _sessions)
                    if (seen < cutoff)
                        _sessions.TryRemove(id, out _);
                return _sessions.Count;
            }
        }
    }

    public event Action<string>? Log;

    public void Start(IMcpEngineBridge bridge, McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        lock (_stateLock)
        {
            if (IsRunning)
                throw new InvalidOperationException("The MCP server is already running.");
            _bridge = bridge;
            _options = options;
            Port = options.Port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/mcp/");
            _listener.Prefixes.Add($"http://localhost:{Port}/mcp/");
            _listener.Start();
            _cts = new CancellationTokenSource();
            IsRunning = true;
            _sessions.Clear();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
            WriteStatusFile();
            Log?.Invoke($"MCP server listening on http://127.0.0.1:{Port}/mcp");
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!IsRunning)
                return;
            IsRunning = false;
            _cts?.Cancel();
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception exception)
            {
                Log?.Invoke($"MCP server stop error: {exception.Message}");
            }
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            _acceptTask = null;
            _bridge = null;
            DeleteStatusFile();
            Log?.Invoke("MCP server stopped");
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (cancellation.IsCancellationRequested || !listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            _ = Task.Run(() => HandleContext(context, cancellation), cancellation);
        }
    }

    private async Task HandleContext(HttpListenerContext context, CancellationToken cancellation)
    {
        try
        {
            string? sessionId = context.Request.Headers["Mcp-Session-Id"];
            if (sessionId is not null)
                _sessions[sessionId] = DateTimeOffset.UtcNow;
            if (context.Request.HttpMethod is "POST")
            {
                using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync(cancellation).ConfigureAwait(false);
                (string response, int status, string? session) = HandleMessage(body, sessionId);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                if (session is not null)
                    context.Response.Headers["Mcp-Session-Id"] = session;
                byte[] payload = Encoding.UTF8.GetBytes(response);
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload, cancellation).ConfigureAwait(false);
            }
            else
            {
                context.Response.StatusCode = 405;
                context.Response.ContentType = "application/json";
                byte[] payload = Encoding.UTF8.GetBytes(JsonRpcError(null, -32600, "Only POST is supported."));
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload, cancellation).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            try
            {
                context.Response.StatusCode = 500;
                byte[] payload = Encoding.UTF8.GetBytes($"{{\"error\":\"{JsonEncoder.Encode(exception.Message)}\"}}");
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload, cancellation).ConfigureAwait(false);
            }
            catch
            {
            }
        }
        finally
        {
            context.Response.Close();
        }
    }

    internal (string Response, int Status, string? Session) HandleMessage(string body, string? sessionId)
    {
        IMcpEngineBridge? bridge = _bridge;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException exception)
        {
            return (JsonRpcError(null, -32700, $"Parse error: {exception.Message}"), 400, null);
        }
        if (root is not JsonObject envelope)
            return (JsonRpcError(null, -32600, "Invalid JSON-RPC envelope."), 400, null);
        string method = envelope["method"]?.GetValue<string>() ?? string.Empty;
        JsonNode? id = envelope["id"];
        bool isNotification = envelope["id"] is null;
        JsonObject? parameters = envelope["params"] as JsonObject;
        try
        {
            JsonNode? result = method switch
            {
                "initialize" => Initialize(parameters),
                "notifications/initialized" => null,
                "ping" => new JsonObject(),
                "tools/list" => ToolsList(),
                "tools/call" => ToolsCall(bridge!, parameters),
                _ => throw new JsonRpcException(-32601, $"Method '{method}' is not supported."),
            };
            if (isNotification)
                return ("", 202, null);
            JsonObject response = new()
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["result"] = result ?? new JsonObject(),
            };
            string responseSession = method == "initialize" ? sessionIdentifier() : sessionId;
            return (response.ToJsonString(JsonSerializerOptions.Default), 200, responseSession);
        }
        catch (JsonRpcException exception)
        {
            return (JsonRpcError(id, exception.Code, exception.Message), 200, sessionId);
        }
        catch (Exception exception)
        {
            return (JsonRpcError(id, -32603, exception.Message), 200, sessionId);
        }
    }

    private static string sessionIdentifier() => Guid.NewGuid().ToString("N");

    private JsonObject Initialize(JsonObject? parameters)
    {
        string requested = parameters?["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion;
        JsonObject result = new()
        {
            ["protocolVersion"] = requested,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = _options.ServerName,
                ["version"] = _options.Version,
            },
            ["instructions"] = "Tools read and mutate the engine editor scene. Start with engine_info and list_objects.",
        };
        return result;
    }

    private static JsonObject ToolsList()
    {
        JsonArray tools = [];
        foreach (McpTool tool in McpTools.All)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchema),
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    private static JsonObject ToolsCall(IMcpEngineBridge? bridge, JsonObject? parameters)
    {
        JsonObject result = new();
        try
        {
            string name = parameters?["name"]?.GetValue<string>() ?? throw new JsonRpcException(-32602, "'name' is required.");
            if (bridge is null)
                throw new JsonRpcException(-32603, "The MCP server has no bridge attached.");
            object? value = McpTools.Call(bridge, name, parameters?["arguments"] as JsonObject is { } arguments
                ? JsonSerializer.Deserialize<JsonElement>(arguments.ToJsonString(JsonSerializerOptions.Default))
                : JsonSerializer.Deserialize<JsonElement>("{}"));
            string text = value switch
            {
                null => "ok",
                string textValue => textValue,
                _ => JsonSerializer.Serialize(value, JsonOptions),
            };
            result["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } };
            result["isError"] = false;
        }
        catch (Exception exception)
        {
            result["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = $"Error: {exception.Message}" } };
            result["isError"] = true;
        }
        return result;
    }

    private static string JsonRpcError(JsonNode? id, int code, string message)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
        return response.ToJsonString(JsonSerializerOptions.Default);
    }

    private void WriteStatusFile()
    {
        if (_options.StatusPath is null)
            return;
        try
        {
            string directory = Path.GetDirectoryName(_options.StatusPath)!;
            if (directory.Length > 0)
                Directory.CreateDirectory(directory);
            JsonObject status = new()
            {
                ["port"] = Port,
                ["pid"] = Environment.ProcessId,
                ["url"] = $"http://127.0.0.1:{Port}/mcp",
            };
            File.WriteAllText(_options.StatusPath, status.ToJsonString(JsonSerializerOptions.Default));
        }
        catch (Exception exception)
        {
            Log?.Invoke($"MCP status file error: {exception.Message}");
        }
    }

    private void DeleteStatusFile()
    {
        if (_options.StatusPath is null)
            return;
        try
        {
            if (File.Exists(_options.StatusPath))
                File.Delete(_options.StatusPath);
        }
        catch
        {
        }
    }

    private sealed class JsonRpcException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    private static class JsonEncoder
    {
        public static string Encode(string value) => System.Text.Json.JsonSerializer.Serialize(value);
    }
}
