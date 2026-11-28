namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class WatcherLifetime : IDisposable
{
    private enum State
    {
        Idle,
        Starting,
        Started,
        Disposed,
    }

    private readonly Lock _lock = new();
    private readonly IConfigurationWatcher _watcher;
    private readonly IDisposable? _ownedResource;
    private State _state;

    public WatcherLifetime(IConfigurationWatcher watcher, IDisposable? ownedResource = null)
    {
        ArgumentNullException.ThrowIfNull(watcher);

        _watcher = watcher;
        _ownedResource = ownedResource;
    }

    public static WatcherLifetime? From(SecretsManagerConfigurationSource source)
    {
        if (source.WatcherFactory is { } factory)
        {
            var watcher = factory();
            return new WatcherLifetime(watcher, watcher as IDisposable);
        }

        if (source.ConfigurationWatcher is { } callerWatcher)
        {
            return new WatcherLifetime(callerWatcher);
        }

        return null;
    }

    public void EnsureStarted(ISecretsManagerConfigurationProvider provider)
    {
        lock (_lock)
        {
            if (_state != State.Idle)
            {
                return;
            }

            _state = State.Starting;
        }

        var started = false;
        try
        {
            _watcher.StartWatching(provider);
            started = true;
        }
        finally
        {
            var release = false;
            lock (_lock)
            {
                if (_state == State.Disposed)
                {
                    release = true;
                }
                else
                {
                    _state = started ? State.Started : State.Idle;
                }
            }

            if (release)
            {
                Release(stop: started);
            }
        }
    }

    public void Dispose()
    {
        bool release;
        bool stop;

        lock (_lock)
        {
            if (_state == State.Disposed)
            {
                return;
            }

            release = _state != State.Starting;
            stop = _state == State.Started;
            _state = State.Disposed;
        }

        if (release)
        {
            Release(stop);
        }
    }

    private void Release(bool stop)
    {
        try
        {
            if (stop)
            {
                _watcher.StopWatching();
            }
        }
        finally
        {
            _ownedResource?.Dispose();
        }
    }
}