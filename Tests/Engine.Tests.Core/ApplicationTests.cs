using Engine.Core;
using Xunit;

namespace Engine.Tests.Core;

public class ApplicationTests
{
    private sealed class RecordingModule(string name, List<string> events) : IUpdatableModule
    {
        private IEngineContext? _context;

        public string Name { get; } = name;
        public int Updates;

        public void Initialize(IEngineContext context)
        {
            _context = context;
            events.Add($"init:{Name}");
        }

        public void Update(in FrameTime frameTime)
        {
            Updates++;
            if (Updates >= 3)
                _context!.RequestExit();
        }

        public void Shutdown() => events.Add($"shutdown:{Name}");
    }

    private sealed class FailingModule : IEngineModule
    {
        public string Name => "Failing";

        public void Initialize(IEngineContext context) => throw new InvalidOperationException("init failure");

        public void Shutdown() { }
    }

    [Fact]
    public void Run_InitializesUpdatesAndShutsDownInOrder()
    {
        List<string> events = [];
        RecordingModule platform = new("Platform", events);
        RecordingModule rhi = new("RHI", events);
        Application application = ApplicationBuilder.Create()
            .AddLoggerSink(new MemoryLogSink())
            .AddModule(platform)
            .AddModule(rhi)
            .Build();
        application.Run();
        Assert.Equal(0, application.Context.ExitCode);
        Assert.Equal(
            new[] { "init:Platform", "init:RHI", "shutdown:RHI", "shutdown:Platform" },
            events);
        Assert.Equal(3, platform.Updates);
        Assert.Equal(3, rhi.Updates);
    }

    [Fact]
    public void PrepareFrame_RunsBeforeEveryUpdateRegardlessOfRegistrationOrder()
    {
        List<string> events = [];
        Application application = ApplicationBuilder.Create()
            .AddLoggerSink(new MemoryLogSink())
            .AddModule(new FrameUpdateProbe(events))
            .AddModule(new FramePreparationProbe(events))
            .Build();
        application.Run();
        Assert.Equal(new[] { "prepare", "update" }, events);
    }

    private sealed class FramePreparationProbe(List<string> events) : IFramePreparationModule
    {
        public string Name => "Preparation";
        public void Initialize(IEngineContext context) { }
        public void PrepareFrame(in FrameTime frameTime) => events.Add("prepare");
        public void Shutdown() { }
    }

    private sealed class FrameUpdateProbe(List<string> events) : IUpdatableModule
    {
        private IEngineContext? _context;
        public string Name => "FrameUpdate";
        public void Initialize(IEngineContext context) => _context = context;
        public void Update(in FrameTime frameTime)
        {
            events.Add("update");
            _context!.RequestExit();
        }
        public void Shutdown() { }
    }

    [Fact]
    public void Run_ModuleFailure_ExitsWithCodeOne()
    {
        Application application = ApplicationBuilder.Create()
            .AddLoggerSink(new MemoryLogSink())
            .AddModule(new FailingModule())
            .Build();
        application.Run();
        Assert.Equal(1, application.Context.ExitCode);
    }

    [Fact]
    public void FixedUpdate_ReceivesStepsAtFixedRate()
    {
        List<int> fixedSteps = [];
        Application application = ApplicationBuilder.Create()
            .AddLoggerSink(new MemoryLogSink())
            .AddModule(new FixedProbe(fixedSteps))
            .AddModule(new OneTickExit())
            .Build();
        application.Run();
        Assert.NotEmpty(fixedSteps);
        Assert.All(fixedSteps, step => Assert.Equal(1, step));
    }

    private sealed class FixedProbe(List<int> steps) : IFixedUpdateModule
    {
        public string Name => "FixedProbe";
        public void Initialize(IEngineContext context) { }
        public void FixedUpdate(in FrameTime frameTime) => steps.Add(1);
        public void Shutdown() { }
    }

    private sealed class OneTickExit : IUpdatableModule
    {
        private IEngineContext? _context;

        public string Name => "OneTickExit";
        public void Initialize(IEngineContext context) => _context = context;
        public void Update(in FrameTime frameTime)
        {
            if (frameTime.TotalTime >= 0.05f)
                _context!.RequestExit();
        }
        public void Shutdown() { }
    }
}
