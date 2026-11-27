using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretsManagerPollingWatcherShould
{
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Now);

    [Test]
    public async Task ThrowWhenStartedTwice()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = ISecretsManagerConfigurationProvider.Mock();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act & assert
        watcher.StartWatching(provider);
        await Assert.That(() => watcher.StartWatching(provider)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ExecuteReloadAfterInterval()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = ISecretsManagerConfigurationProvider.Mock();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act
        watcher.StartWatching(provider);

        // assert
        // 1st refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Once);

        // 2nd refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Exactly(2));
    }

    [Test]
    public async Task NotSwallowException()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = ISecretsManagerConfigurationProvider.Mock();
        provider.Reload().Throws(new InvalidOperationException("Test exception"));

        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act
        watcher.StartWatching(provider);

        // assert
        await Assert.That(() => _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)))).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task NotExecuteReloadAfterSopped()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = ISecretsManagerConfigurationProvider.Mock();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act & assert
        watcher.StartWatching(provider);

        // 1st refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Once);

        // stop watching
        watcher.StopWatching();

        // 2nd refresh should not be called
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Once);
    }

    [Test]
    public async Task ThrowWhenMaxJitterIsNegative()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);
        var maxJitter = TimeSpan.FromSeconds(-1);

        // act & assert
        await Assert.That(() => new SecretsManagerPollingWatcher(interval, maxJitter, _timeProvider))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ExecuteReloadWithinJitterWindow()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);
        var maxJitter = TimeSpan.FromMinutes(1);

        var provider = ISecretsManagerConfigurationProvider.Mock();
        var watcher = new SecretsManagerPollingWatcher(interval, maxJitter, _timeProvider);

        // act
        watcher.StartWatching(provider);

        // assert
        // jitter is non-negative, no reload before interval
        _timeProvider.Advance(interval.Subtract(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Exactly(0));

        // 1st refresh: due time within [interval, interval + maxJitter]
        _timeProvider.Advance(maxJitter.Add(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Once);

        // 2nd refresh: rescheduled with fresh jitter
        _timeProvider.Advance(interval.Add(maxJitter));
        provider.Reload().WasCalled(Times.Exactly(2));

        // 3rd refresh: rescheduling continues
        _timeProvider.Advance(interval.Add(maxJitter));
        provider.Reload().WasCalled(Times.Exactly(3));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ThrowWhenStartedAfterDispose(bool asyncDispose)
    {
        // arrange
        var provider = new CountingProvider();
        var watcher = new SecretsManagerPollingWatcher(TimeSpan.FromMinutes(5), _timeProvider);

        // act
        if (asyncDispose) await watcher.DisposeAsync();
        else watcher.Dispose();

        // assert
        await Assert.That(() => watcher.StartWatching(provider)).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task ThrowObjectDisposedInsteadOfAlreadyStartedWhenStartedWatcherIsDisposed()
    {
        // arrange
        var provider = new CountingProvider();
        var watcher = new SecretsManagerPollingWatcher(TimeSpan.FromMinutes(5), _timeProvider);
        watcher.StartWatching(provider);

        // act
        watcher.Dispose();

        // assert
        await Assert.That(() => watcher.StartWatching(provider)).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task BehaveAsStoppedAfterDispose()
    {
        // arrange
        var provider = new CountingProvider();
        var watcher = new SecretsManagerPollingWatcher(TimeSpan.FromMinutes(5), _timeProvider);
        watcher.StartWatching(provider);

        // act & assert
        watcher.Dispose();
        await Assert.That(() => watcher.StopWatching()).ThrowsNothing();
    }

    [Test]
    public async Task BeIdempotentWhenDisposed()
    {
        // arrange
        var provider = new CountingProvider();
        var watcher = new SecretsManagerPollingWatcher(TimeSpan.FromMinutes(5), _timeProvider);
        watcher.StartWatching(provider);

        // act & assert
        await Assert.That(() => watcher.Dispose()).ThrowsNothing();
        await Assert.That(() => watcher.Dispose()).ThrowsNothing();
        await Assert.That(async () => await watcher.DisposeAsync()).ThrowsNothing();
        await Assert.That(async () => await watcher.DisposeAsync()).ThrowsNothing();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NotExecuteReloadAfterDispose(bool asyncDispose)
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = new CountingProvider();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);
        watcher.StartWatching(provider);

        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        await Assert.That(provider.ReloadCalls).IsEqualTo(1);

        // act
        if (asyncDispose) await watcher.DisposeAsync();
        else watcher.Dispose();
        _timeProvider.Advance(interval * 3);

        // assert
        await Assert.That(provider.ReloadCalls).IsEqualTo(1);
    }

    [Test]
    public async Task NotExecuteReloadAgainWhenDisposedFromReloadCallback()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        SecretsManagerPollingWatcher? watcher = null;
        var provider = new CountingProvider { OnReload = () => watcher!.Dispose() };
        watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);
        watcher.StartWatching(provider);

        // act
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        _timeProvider.Advance(interval * 3);

        // assert
        await Assert.That(provider.ReloadCalls).IsEqualTo(1);
    }

    [Test]
    public async Task AllowOnlyOneOfConcurrentStarts()
    {
        var guard = TimeSpan.FromSeconds(10);
        var time = new BlockingTimeProvider();
        var provider = new CountingProvider();
        using var watcher = new SecretsManagerPollingWatcher(TimeSpan.FromMinutes(5), time);
        var first = Task.Run(() => Capture(() => watcher.StartWatching(provider)));
        Task<Exception?>? second = null;
        try
        {
            await time.Entered.Task.WaitAsync(guard);
            Thread? thread = null;
            var entering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            second = Task.Run(() =>
            {
                thread = Thread.CurrentThread;
                entering.SetResult();
                return Capture(() => watcher.StartWatching(provider));
            });
            await entering.Task.WaitAsync(guard);
            WaitUntilBlockedOrCompleted(second, thread!, guard);
        }
        finally
        {
            time.Release.TrySetResult();
        }

        var firstFailure = await first.WaitAsync(guard);
        var secondFailure = await second!.WaitAsync(guard);
        await Assert.That(firstFailure).IsNull();
        await Assert.That(secondFailure).IsTypeOf<InvalidOperationException>();
        await Assert.That(secondFailure!.Message).Contains("already started");
        await Assert.That(time.CreatedTimers.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DisposeTimerOfInterleavedStartWhenDisposeIsCalled() =>
        await AssertDisposeWaitsForStartHoldingTheLock((watcher, beforeDispose) => Task.Run(() =>
        {
            beforeDispose();
            watcher.Dispose();
        }));

    [Test]
    public async Task DisposeTimerOfInterleavedStartWhenDisposeAsyncIsCalled() =>
        await AssertDisposeWaitsForStartHoldingTheLock((watcher, beforeDispose) => Task.Run(async () =>
        {
            beforeDispose();

            // takes the watcher lock in the synchronous part, before any await
            await watcher.DisposeAsync();
        }));

    private static async Task AssertDisposeWaitsForStartHoldingTheLock(
        Func<SecretsManagerPollingWatcher, Action, Task> startDispose)
    {
        // arrange
        var hangGuard = TimeSpan.FromSeconds(10);
        var interval = TimeSpan.FromMinutes(5);

        var time = new BlockingTimeProvider();
        var provider = new CountingProvider();
        var watcher = new SecretsManagerPollingWatcher(interval, time);

        // act
        // worker A is inside CreateTimer, holding the watcher lock
        var start = Task.Run(() => watcher.StartWatching(provider));
        Task? dispose = null;
        try
        {
            await time.Entered.Task.WaitAsync(hangGuard);
            Thread? disposeThread = null;
            var enteringDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dispose = startDispose(watcher, () =>
            {
                disposeThread = Thread.CurrentThread;
                enteringDispose.SetResult();
            });
            await enteringDispose.Task.WaitAsync(hangGuard);
            WaitUntilBlockedOrCompleted(dispose, disposeThread!, hangGuard);
        }
        finally
        {
            time.Release.TrySetResult();
        }

        await start.WaitAsync(TimeSpan.FromSeconds(10));
        await dispose!.WaitAsync(TimeSpan.FromSeconds(10));

        time.Inner.Advance(interval * 3);

        // assert: start succeeded first, then disposal disposed the timer it created
        await Assert.That(time.CreatedTimers.Count).IsEqualTo(1);
        await Assert.That(time.CreatedTimers[0].DisposeCalls).IsEqualTo(1);
        await Assert.That(provider.ReloadCalls).IsEqualTo(0);
        await Assert.That(() => watcher.StartWatching(provider)).ThrowsExactly<ObjectDisposedException>();
    }

    private static void WaitUntilBlockedOrCompleted(Task task, Thread thread, TimeSpan guard)
    {
        var stopwatch = Stopwatch.StartNew();
        var spinner = new SpinWait();
        while (!task.IsCompleted && (thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0)
        {
            if (stopwatch.Elapsed > guard)
            {
                throw new TimeoutException("Worker neither completed nor blocked on the watcher lock.");
            }

            spinner.SpinOnce(sleep1Threshold: -1);
        }
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // CreateTimer signals that it has been entered and blocks until released, then delegates to FakeTimeProvider
    private sealed class BlockingTimeProvider : TimeProvider
    {
        private readonly List<TrackingTimer> _createdTimers = [];

        public FakeTimeProvider Inner { get; } = new(DateTimeOffset.UtcNow);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<TrackingTimer> CreatedTimers
        {
            get
            {
                lock (_createdTimers)
                {
                    return _createdTimers.ToArray();
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Entered.TrySetResult();
            Release.Task.Wait(TimeSpan.FromSeconds(10));

            var timer = new TrackingTimer(Inner.CreateTimer(callback, state, dueTime, period));
            lock (_createdTimers)
            {
                _createdTimers.Add(timer);
            }

            return timer;
        }
    }

    private sealed class TrackingTimer : ITimer
    {
        private readonly ITimer _inner;
        private int _disposeCalls;

        public TrackingTimer(ITimer inner)
        {
            _inner = inner;
        }

        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        public bool Change(TimeSpan dueTime, TimeSpan period) => _inner.Change(dueTime, period);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCalls);
            _inner.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCalls);
            await _inner.DisposeAsync();
        }
    }

    private sealed class CountingProvider : ISecretsManagerConfigurationProvider
    {
        private int _reloadCalls;

        public Action? OnReload { get; init; }

        public int ReloadCalls => Volatile.Read(ref _reloadCalls);

        public SecretsManagerConfigurationSource Source { get; } = new();

        public void Load()
        {
        }

        public void Reload()
        {
            Interlocked.Increment(ref _reloadCalls);
            OnReload?.Invoke();
        }
    }
}
