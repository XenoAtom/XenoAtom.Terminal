// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace XenoAtom.Terminal.Internal;

internal sealed class TerminalEventBroadcaster : IDisposable
{
    private sealed class Subscription : IDisposable
    {
        private readonly TerminalEventBroadcaster? _owner;
        public readonly int Id;
        public readonly Channel<TerminalEvent> Channel;

        public Subscription(TerminalEventBroadcaster? owner, int id, Channel<TerminalEvent> channel)
        {
            _owner = owner;
            Id = id;
            Channel = channel;
        }

        public void Dispose() => _owner?.Unsubscribe(Id);
    }

    private readonly ConcurrentDictionary<int, ChannelWriter<TerminalEvent>> _writers = new();
    private readonly object _defaultLock = new();
    private int _nextId;
    private bool _completed;

    private Subscription? _defaultSubscription;
    private const int DefaultBufferCapacity = 1024;

    public void Publish(TerminalEvent ev)
    {
        if (ev is null)
        {
            throw new ArgumentNullException(nameof(ev));
        }

        if (Volatile.Read(ref _completed))
        {
            return;
        }

        EnsureDefaultSubscription();

        foreach (var pair in _writers)
        {
            pair.Value.TryWrite(ev);
        }
    }

    public IDisposable Subscribe(out ChannelReader<TerminalEvent> reader)
    {
        if (Volatile.Read(ref _completed))
        {
            var completed = Channel.CreateUnbounded<TerminalEvent>();
            completed.Writer.TryComplete();
            reader = completed.Reader;
            return new Subscription(owner: null, id: 0, completed);
        }

        var id = Interlocked.Increment(ref _nextId);
        var channel = Channel.CreateUnbounded<TerminalEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _writers.TryAdd(id, channel.Writer);
        reader = channel.Reader;
        return new Subscription(this, id, channel);
    }

    public bool TryReadEvent(out TerminalEvent ev)
    {
        var reader = GetDefaultReader();
        return reader.TryRead(out ev!);
    }

    public ValueTask<TerminalEvent> ReadEventAsync(CancellationToken cancellationToken)
    {
        var reader = GetDefaultReader();
        return reader.ReadAsync(cancellationToken);
    }

    public async IAsyncEnumerable<TerminalEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscription = Subscribe(out var reader);
        try
        {
            await foreach (var ev in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return ev;
            }
        }
        finally
        {
            subscription.Dispose();
        }
    }

    public void Complete(Exception? exception = null)
    {
        if (Interlocked.Exchange(ref _completed, true))
        {
            return;
        }

        foreach (var pair in _writers)
        {
            pair.Value.TryComplete(exception);
        }

        _writers.Clear();
    }

    private ChannelReader<TerminalEvent> GetDefaultReader()
    {
        return EnsureDefaultSubscription().Channel.Reader;
    }

    private Subscription EnsureDefaultSubscription()
    {
        if (_defaultSubscription is not null)
        {
            return _defaultSubscription;
        }

        lock (_defaultLock)
        {
            if (_defaultSubscription is not null)
            {
                return _defaultSubscription;
            }

            if (Volatile.Read(ref _completed))
            {
                var completed = Channel.CreateUnbounded<TerminalEvent>();
                completed.Writer.TryComplete();
                var completedSubscription = new Subscription(owner: null, id: 0, completed);
                _defaultSubscription = completedSubscription;
                return completedSubscription;
            }

            var id = Interlocked.Increment(ref _nextId);
            var channel = Channel.CreateBounded<TerminalEvent>(new BoundedChannelOptions(DefaultBufferCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            });

            _writers.TryAdd(id, channel.Writer);
            var subscription = new Subscription(this, id, channel);
            _defaultSubscription = subscription;
            return subscription;
        }
    }

    private void Unsubscribe(int id)
    {
        if (_writers.TryRemove(id, out var writer))
        {
            writer.TryComplete();
        }
    }

    public void Dispose() => Complete();
}
