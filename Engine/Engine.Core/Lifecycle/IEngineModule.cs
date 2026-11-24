namespace Engine.Core;

public interface IEngineModule
{
    string Name { get; }

    void Initialize(IEngineContext context);

    void Shutdown();
}

public interface IUpdatableModule : IEngineModule
{
    void Update(in FrameTime frameTime);
}

public interface IFramePreparationModule : IEngineModule
{
    void PrepareFrame(in FrameTime frameTime);
}

public interface IFixedUpdateModule : IEngineModule
{
    void FixedUpdate(in FrameTime frameTime);
}
