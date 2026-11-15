namespace Engine.Core;

public interface IEventBus
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);

    void Unsubscribe<TEvent>(Action<TEvent> handler);

    void Publish<TEvent>(TEvent evt);

    void PublishDeferred<TEvent>(TEvent evt);

    void ProcessDeferred();
}
