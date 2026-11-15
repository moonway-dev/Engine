namespace Engine.Core;

public interface IEngineContext
{
    IServiceRegistry Services { get; }

    EngineConfiguration Configuration { get; }

    LoggerFactory Logging { get; }

    GameClock Clock { get; }

    IEventBus Events { get; }

    MainThreadDispatcher Dispatcher { get; }

    bool IsExitRequested { get; }

    int ExitCode { get; }

    void RequestExit(int exitCode = 0);
}
