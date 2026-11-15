using Engine.Core;
using Xunit;

namespace Engine.Tests.Core;

public class EventBusTests
{
    [Fact]
    public void Publish_DeliversToAllSubscribers()
    {
        EventBus bus = new();
        List<int> first = [];
        List<int> second = [];
        bus.Subscribe<int>(first.Add);
        bus.Subscribe<int>(second.Add);
        bus.Publish(7);
        bus.Publish(9);
        Assert.Equal(new[] { 7, 9 }, first);
        Assert.Equal(new[] { 7, 9 }, second);
    }

    [Fact]
    public void Unsubscribe_StopsDelivery()
    {
        EventBus bus = new();
        List<string> received = [];
        Action<string> handler = received.Add;
        bus.Subscribe(handler);
        bus.Publish("a");
        bus.Unsubscribe(handler);
        bus.Publish("b");
        Assert.Equal(["a"], received);
    }

    [Fact]
    public void SubscriptionDispose_StopsDelivery()
    {
        EventBus bus = new();
        List<int> received = [];
        using IDisposable subscription = bus.Subscribe<int>(received.Add);
        bus.Publish(1);
        subscription.Dispose();
        bus.Publish(2);
        Assert.Equal([1], received);
    }

    [Fact]
    public void DeferredPublish_DeliveredAfterProcess()
    {
        EventBus bus = new();
        List<string> received = [];
        bus.Subscribe<string>(received.Add);
        bus.PublishDeferred("later");
        Assert.Empty(received);
        bus.ProcessDeferred();
        Assert.Equal(["later"], received);
    }

    [Fact]
    public void HandlerException_DoesNotStopOtherHandlers()
    {
        EventBus bus = new();
        List<int> received = [];
        bus.Subscribe<int>(_ => throw new InvalidOperationException("bad handler"));
        bus.Subscribe<int>(received.Add);
        Assert.Throws<AggregateException>(() => bus.Publish(1));
        Assert.Equal([1], received);
    }
}

public class MainThreadDispatcherTests
{
    [Fact]
    public void Post_QueuesAndProcesses()
    {
        MainThreadDispatcher dispatcher = new();
        int executed = 0;
        dispatcher.Post(() => executed++);
        dispatcher.Post(() => executed++);
        Assert.Equal(2, dispatcher.PendingCount);
        dispatcher.Process();
        Assert.Equal(2, executed);
        Assert.Equal(0, dispatcher.PendingCount);
    }
}
