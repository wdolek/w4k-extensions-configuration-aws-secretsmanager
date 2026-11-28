using Microsoft.Extensions.Time.Testing;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class WatcherLifetimeShould
{
    [Test]
    public async Task ReturnNullWhenSourceHasNoWatcher()
    {
        // arrange
        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret" };

        // act
        var lifetime = WatcherLifetime.From(source);

        // assert
        await Assert.That(lifetime).IsNull();
    }

    [Test]
    public async Task StartOnceAndStopWithoutDisposingCallerWatcher()
    {
        // arrange
        var watcher = new RecordingWatcher();
        var provider = ISecretsManagerConfigurationProvider.Mock().Object;
        var source = new SecretsManagerConfigurationBuilder("le-secret")
            .WithConfigurationWatcher(watcher)
            .Build();

        var lifetime = WatcherLifetime.From(source)!;

        // act
        lifetime.EnsureStarted(provider);
        lifetime.EnsureStarted(provider);
        lifetime.Dispose();
        lifetime.Dispose();
        lifetime.EnsureStarted(provider);

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task DisposeOwnedWatcherEvenWhenNeverStarted()
    {
        // arrange
        var watcher = new RecordingWatcher();
        using var lifetime = new WatcherLifetime(watcher, watcher);

        // act
        lifetime.Dispose();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(0);
        await Assert.That(watcher.StopCalls).IsEqualTo(0);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task NotStopUnownedWatcherWhenNeverStarted()
    {
        // arrange
        var watcher = new RecordingWatcher();
        using var lifetime = new WatcherLifetime(watcher);

        // act
        lifetime.Dispose();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(0);
        await Assert.That(watcher.StopCalls).IsEqualTo(0);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task RetryStartAfterFailureAndOnlyStopOnceStarted()
    {
        // arrange
        var attempts = 0;
        var watcher = new RecordingWatcher
        {
            OnStart = _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("transient start failure");
                }
            },
        };

        var provider = ISecretsManagerConfigurationProvider.Mock().Object;
        using var lifetime = new WatcherLifetime(watcher);

        // act
        await Assert.That(() => lifetime.EnsureStarted(provider)).ThrowsExactly<InvalidOperationException>();

        lifetime.EnsureStarted(provider);
        lifetime.EnsureStarted(provider);
        lifetime.Dispose();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(2);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
    }

    [Test]
    public async Task StopAndDisposeAfterStartReturnsWhenDisposedReentrantly()
    {
        // arrange
        WatcherLifetime? lifetime = null;
        var stopCalledDuringStart = false;
        var isInStart = false;

        var watcher = new RecordingWatcher
        {
            OnStart = _ =>
            {
                isInStart = true;
                lifetime!.Dispose();
                isInStart = false;
            },
            OnStop = () => stopCalledDuringStart = isInStart,
        };

        var provider = ISecretsManagerConfigurationProvider.Mock().Object;
        lifetime = new WatcherLifetime(watcher, watcher);

        // act
        lifetime.EnsureStarted(provider);

        // assert
        await Assert.That(stopCalledDuringStart).IsFalse();
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task StopAndDisposeAfterStartReturnsWhenDisposedConcurrently()
    {
        // arrange
        var enteredStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var watcher = new RecordingWatcher
        {
            OnStart = _ =>
            {
                enteredStart.TrySetResult();
                releaseStart.Task.Wait(TimeSpan.FromSeconds(5));
            },
        };

        var provider = ISecretsManagerConfigurationProvider.Mock().Object;
        var lifetime = new WatcherLifetime(watcher, watcher);

        // act
        var startTask = Task.Run(() => lifetime.EnsureStarted(provider));
        await enteredStart.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lifetime.Dispose();
        releaseStart.SetResult();
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task HonourLastConfiguredWatcherBetweenPollingAndCallerOwned()
    {
        // arrange
        var callerWatcher = new RecordingWatcher();
        var provider = ISecretsManagerConfigurationProvider.Mock().Object;

        // polling -> caller-owned via property
        var sourceWithCaller = new SecretsManagerConfigurationBuilder("le-secret")
            .WithPollingWatcher(TimeSpan.FromMinutes(5), new FakeTimeProvider())
            .Build();
        sourceWithCaller.ConfigurationWatcher = callerWatcher;

        // act
        using (var lifetime = WatcherLifetime.From(sourceWithCaller)!)
        {
            lifetime.EnsureStarted(provider);
        }

        // assert
        await Assert.That(callerWatcher.StartCalls).IsEqualTo(1);
        await Assert.That(callerWatcher.StopCalls).IsEqualTo(1);
        await Assert.That(callerWatcher.DisposeCalls).IsEqualTo(0);

        // caller-owned -> polling via builder
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var unusedCaller = new RecordingWatcher();
        var sourceWithPolling = new SecretsManagerConfigurationBuilder("le-secret")
            .WithConfigurationWatcher(unusedCaller)
            .WithPollingWatcher(TimeSpan.FromMinutes(5), time)
            .Build();

        using (var lifetime = WatcherLifetime.From(sourceWithPolling)!)
        {
            lifetime.EnsureStarted(provider);
        }

        await Assert.That(unusedCaller.StartCalls).IsEqualTo(0);
    }

    internal sealed class RecordingWatcher : IConfigurationWatcher, IDisposable
    {
        private int _starts;
        private int _stops;
        private int _disposes;

        public Action<ISecretsManagerConfigurationProvider>? OnStart { get; init; }

        public Action? OnStop { get; init; }

        public int StartCalls => Volatile.Read(ref _starts);

        public int StopCalls => Volatile.Read(ref _stops);

        public int DisposeCalls => Volatile.Read(ref _disposes);

        public void StartWatching(ISecretsManagerConfigurationProvider provider)
        {
            Interlocked.Increment(ref _starts);
            OnStart?.Invoke(provider);
        }

        public void StopWatching()
        {
            Interlocked.Increment(ref _stops);
            OnStop?.Invoke();
        }

        public void Dispose() => Interlocked.Increment(ref _disposes);
    }
}