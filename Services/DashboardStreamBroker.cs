using System.Threading.Channels;
using TradingViewWebhookDashboard.Models;

namespace TradingViewWebhookDashboard.Services;

public sealed class DashboardStreamBroker
{
    private readonly object _gate = new();
    private readonly List<Channel<DashboardSnapshot>> _subscribers = [];

    public DashboardStreamSubscription Subscribe()
    {
        var channel = Channel.CreateUnbounded<DashboardSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        return new DashboardStreamSubscription(channel, () => Remove(channel));
    }

    public void Publish(DashboardSnapshot snapshot)
    {
        List<Channel<DashboardSnapshot>> subscribers;

        lock (_gate)
        {
            subscribers = [.. _subscribers];
        }

        foreach (var subscriber in subscribers)
        {
            if (!subscriber.Writer.TryWrite(snapshot))
            {
                Remove(subscriber);
            }
        }
    }

    private void Remove(Channel<DashboardSnapshot> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }
}

public sealed class DashboardStreamSubscription : IAsyncDisposable
{
    private readonly Channel<DashboardSnapshot> _channel;
    private readonly Action _disposeAction;
    private bool _disposed;

    public DashboardStreamSubscription(Channel<DashboardSnapshot> channel, Action disposeAction)
    {
        _channel = channel;
        _disposeAction = disposeAction;
    }

    public ChannelReader<DashboardSnapshot> Reader => _channel.Reader;

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _disposeAction();
        return ValueTask.CompletedTask;
    }
}
