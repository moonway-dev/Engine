namespace Engine.Core;

public sealed class ApplicationBuilder
{
    private readonly List<IEngineModule> _modules = [];
    private readonly List<ILogSink> _logSinks = [];
    private Action<IServiceRegistry>? _serviceConfiguration;

    public static ApplicationBuilder Create() => new();

    public ApplicationBuilder AddLoggerSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _logSinks.Add(sink);
        return this;
    }

    public ApplicationBuilder ConfigureServices(Action<IServiceRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _serviceConfiguration = _serviceConfiguration is null ? configure : services =>
        {
            _serviceConfiguration(services);
            configure(services);
        };
        return this;
    }

    public ApplicationBuilder AddModule(IEngineModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _modules.Add(module);
        return this;
    }

    public Application Build()
    {
        ServiceRegistry services = new();
        _serviceConfiguration?.Invoke(services);
        LoggerFactory logging = _logSinks.Count > 0 ? new LoggerFactory([.. _logSinks]) : new LoggerFactory(new ConsoleLogSink());
        EngineContext context = new(
            services,
            new EngineConfiguration(),
            logging,
            new GameClock(),
            new EventBus(),
            new MainThreadDispatcher());
        services.RegisterSingleton<IEngineContext>(context);
        return new Application(context, _modules);
    }
}
