namespace Engine.Core;

public sealed class EngineContext(
    IServiceRegistry services,
    EngineConfiguration configuration,
    LoggerFactory logging,
    GameClock clock,
    IEventBus events,
    MainThreadDispatcher dispatcher) : IEngineContext
{
    public IServiceRegistry Services { get; } = services;
    public EngineConfiguration Configuration { get; } = configuration;
    public LoggerFactory Logging { get; } = logging;
    public GameClock Clock { get; } = clock;
    public IEventBus Events { get; } = events;
    public MainThreadDispatcher Dispatcher { get; } = dispatcher;
    public bool IsExitRequested { get; private set; }
    public int ExitCode { get; private set; }

    public void RequestExit(int exitCode = 0)
    {
        if (IsExitRequested)
            return;
        IsExitRequested = true;
        ExitCode = exitCode;
    }
}
