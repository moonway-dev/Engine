using System.Diagnostics;

namespace Engine.Core;

public sealed class Application
{
    public const string LogCategory = "Application";

    private readonly IEngineModule[] _modules;
    private readonly List<IUpdatableModule> _updatableModules;
    private readonly List<IFixedUpdateModule> _fixedUpdateModules;
    private readonly List<IFramePreparationModule> _framePreparationModules;
    private readonly Stopwatch _stopwatch = new();
    private double _lastRawTime;

    public IEngineContext Context { get; }

    internal Application(EngineContext context, IEnumerable<IEngineModule> modules)
    {
        Context = context;
        _modules = [.. modules];
        _updatableModules = [.. _modules.OfType<IUpdatableModule>()];
        _fixedUpdateModules = [.. _modules.OfType<IFixedUpdateModule>()];
        _framePreparationModules = [.. _modules.OfType<IFramePreparationModule>()];
    }

    public void Run()
    {
        ILogger log = Context.Logging.CreateLogger(LogCategory);
        try
        {
            log.Information($"Engine {EngineInfo.EngineVersion} starting ({EngineInfo.RuntimeDescription}, {EngineInfo.OperatingSystem})");
            InitializeModules(log);
            _stopwatch.Restart();
            _lastRawTime = _stopwatch.Elapsed.TotalSeconds;
            RunLoop();
            log.Information($"Engine stopped with exit code {Context.ExitCode}");
        }
        catch (Exception exception)
        {
            log.Critical("Unhandled engine exception, writing crash report", exception);
            string reportPath = CrashReport.Write(
                Path.Combine("Logs", "Crashes"),
                exception,
                $"Frames rendered: {Context.Clock.FrameIndex}");
            log.Error($"Crash report written to {Path.GetFullPath(reportPath)}");
            Context.RequestExit(1);
        }
        finally
        {
            ShutdownModules(log);
            Context.Logging.Flush();
            Context.Logging.Dispose();
        }
    }

    private void InitializeModules(ILogger log)
    {
        foreach (IEngineModule module in _modules)
        {
            log.Debug($"Initializing module '{module.Name}'");
            module.Initialize(Context);
        }
    }

    private void RunLoop()
    {
        while (!Context.IsExitRequested)
        {
            double rawTime = _stopwatch.Elapsed.TotalSeconds;
            double rawDelta = rawTime - _lastRawTime;
            _lastRawTime = rawTime;
            Context.Clock.Tick(rawDelta);
            Context.Dispatcher.Process();
            FrameTime fixedFrameTime = new(
                Context.Clock.DeltaTime,
                Context.Clock.UnscaledDeltaTime,
                Context.Clock.TotalTime,
                Context.Clock.FixedDeltaTime,
                Context.Clock.FrameIndex);
            while (Context.Clock.ConsumeFixedStep())
            {
                foreach (IFixedUpdateModule module in _fixedUpdateModules)
                    module.FixedUpdate(fixedFrameTime);
            }
            FrameTime frameTime = new(
                Context.Clock.DeltaTime,
                Context.Clock.UnscaledDeltaTime,
                Context.Clock.TotalTime,
                Context.Clock.FixedDeltaTime,
                Context.Clock.FrameIndex,
                Context.Clock.FixedInterpolationAlpha,
                Context.Clock.FixedStepsTakenThisFrame,
                Context.Clock.FixedStepsPending,
                Context.Clock.DroppedFixedTimeSecondsThisFrame);
            foreach (IFramePreparationModule module in _framePreparationModules)
                module.PrepareFrame(frameTime);
            foreach (IUpdatableModule module in _updatableModules)
                module.Update(frameTime);
        }
    }

    private void ShutdownModules(ILogger log)
    {
        for (int i = _modules.Length - 1; i >= 0; i--)
        {
            try
            {
                log.Debug($"Shutting down module '{_modules[i].Name}'");
                _modules[i].Shutdown();
            }
            catch (Exception exception)
            {
                log.Error($"Module '{_modules[i].Name}' failed to shut down", exception);
            }
        }
    }
}
