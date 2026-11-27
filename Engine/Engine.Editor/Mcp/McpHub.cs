using Engine.Mcp;

namespace Engine.Editor.Mcp;

public sealed class McpHub : IDisposable
{
    private readonly McpServer _server = new();
    private IMcpEngineBridge? _bridge;
    private bool _disposed;

    public McpServer Server => _server;

    public bool Enabled { get; private set; }

    public int Port { get; set; } = 8777;

    public string? StatusPath { get; set; }

    public event Action<string>? Log;

    public void Attach(IMcpEngineBridge bridge) => _bridge = bridge;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_bridge is null || _server.IsRunning)
            return;
        _server.Log += OnServerLog;
        _server.Start(_bridge, new McpServerOptions
        {
            Port = Port,
            ServerName = "Engine",
            StatusPath = StatusPath,
        });
        Enabled = true;
    }

    public void Stop()
    {
        if (!_server.IsRunning)
            return;
        _server.Stop();
        _server.Log -= OnServerLog;
        Enabled = false;
    }

    public void Toggle(bool enable)
    {
        if (enable)
            Start();
        else
            Stop();
    }

    public void SetPort(int port)
    {
        Port = Math.Clamp(port, 1024, 65535);
        if (_server.IsRunning)
        {
            Stop();
            Start();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _server.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnServerLog(string message) => Log?.Invoke(message);
}
