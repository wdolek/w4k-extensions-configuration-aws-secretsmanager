using Amazon;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretsManagerConfigurationProviderDisposalShould
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private static GetSecretValueResponse FirstSecret => new()
    {
        VersionId = "d6d1b757d46d449d1835a10869dfb9d1",
        SecretString = """{ "Key": "one" }""",
    };

    private static GetSecretValueResponse SecondSecret => new()
    {
        VersionId = "d6d1b757d46d449d1835a10869dfb9d2",
        SecretString = """{ "Key": "two" }""",
    };

    [Test]
    public async Task StopPollingAndDisposeTimerWhenConfigurationRootIsDisposed()
    {
        // arrange
        var time = new TrackingTimeProvider();
        var client = new FakeSecretsClient((_, _) => Task.FromResult(FirstSecret));

        var root = new ConfigurationBuilder()
            .AddSecretsManager("le-secret", b => b.WithSecretsManager(client).WithPollingWatcher(Interval, time))
            .Build();

        time.Inner.Advance(Interval + TimeSpan.FromSeconds(1));
        await Assert.That(client.Calls).IsEqualTo(2);
        await Assert.That(time.TimerDisposeCalls).IsEqualTo(0);

        // act
        ((IDisposable)root).Dispose();
        time.Inner.Advance(Interval * 3);

        // assert
        await Assert.That(client.Calls).IsEqualTo(2);
        await Assert.That(time.TimerDisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task StopPollingWhenConfigurationManagerIsDisposed()
    {
        // arrange
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new FakeSecretsClient((_, _) => Task.FromResult(FirstSecret));

        var manager = new ConfigurationManager();
        manager.AddSecretsManager("le-secret", (_, b) => b.WithSecretsManager(client).WithPollingWatcher(Interval, time));

        time.Advance(Interval + TimeSpan.FromSeconds(1));
        await Assert.That(client.Calls).IsEqualTo(2);

        // act
        manager.Dispose();
        time.Advance(Interval * 3);

        // assert
        await Assert.That(client.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task StopButNotDisposeCallerSuppliedWatcher()
    {
        // arrange
        var watcher = new WatcherLifetimeShould.RecordingWatcher();
        var client = new FakeSecretsClient((_, _) => Task.FromResult(FirstSecret));

        var root = new ConfigurationBuilder()
            .AddSecretsManager("le-secret", b => b.WithSecretsManager(client).WithConfigurationWatcher(watcher))
            .Build();

        // act
        ((IDisposable)root).Dispose();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task StartWatcherOnlyOnceWhenConfigurationRootIsReloaded()
    {
        // arrange
        var watcher = new WatcherLifetimeShould.RecordingWatcher();
        var client = new FakeSecretsClient((_, _) => Task.FromResult(FirstSecret));

        using var root = (ConfigurationRoot)new ConfigurationBuilder()
            .AddSecretsManager("le-secret", b => b.WithSecretsManager(client).WithConfigurationWatcher(watcher))
            .Build();

        // act
        root.Reload();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(root["Key"]).IsEqualTo("one");
    }

    [Test]
    public async Task StartWatcherOnceAfterIgnoredInitialFailureResolvesOnLaterLoad()
    {
        // arrange
        var watcher = new WatcherLifetimeShould.RecordingWatcher();
        var client = new FakeSecretsClient((call, _) => call == 1
            ? throw new ResourceNotFoundException("missing")
            : Task.FromResult(FirstSecret));

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = client,
            ConfigurationWatcher = watcher,
            OnLoadException = ctx => ctx.Ignore = true,
        };

        using var provider = new SecretsManagerConfigurationProvider(source);

        // act
        provider.Load();
        await Assert.That(watcher.StartCalls).IsEqualTo(0);

        provider.Load();
        provider.Load();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
    }

    [Test]
    public async Task CaptureWatcherAtProviderConstruction()
    {
        // arrange
        var original = new WatcherLifetimeShould.RecordingWatcher();
        var replacement = new WatcherLifetimeShould.RecordingWatcher();

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = new FakeSecretsClient((_, _) => Task.FromResult(FirstSecret)),
            ConfigurationWatcher = original,
        };

        var provider = new SecretsManagerConfigurationProvider(source);
        source.ConfigurationWatcher = replacement;

        // act
        provider.Load();
        provider.Dispose();

        // assert
        await Assert.That(original.StartCalls).IsEqualTo(1);
        await Assert.That(original.StopCalls).IsEqualTo(1);
        await Assert.That(replacement.StartCalls).IsEqualTo(0);
    }

    [Test]
    public async Task IgnoreReloadAfterDispose()
    {
        // arrange
        var client = new FakeSecretsClient((_, _) => Task.FromResult(FirstSecret));
        var provider = new SecretsManagerConfigurationProvider(
            new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = client });

        provider.Load();
        var reloadToken = provider.GetReloadToken();
        provider.Dispose();

        // act
        provider.Reload();

        // assert
        await Assert.That(client.Calls).IsEqualTo(1);
        await Assert.That(reloadToken.HasChanged).IsFalse();
    }

    [Test]
    public async Task ThrowObjectDisposedOnLoadAfterDispose()
    {
        // arrange
        var client = new FakeSecretsClient((_, _) => Task.FromResult(FirstSecret));
        var provider = new SecretsManagerConfigurationProvider(
            new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = client });

        provider.Load();
        provider.Dispose();

        // act & assert
        await Assert.That(() => provider.Load()).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(client.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task CancelInFlightReloadAndUnblockJoiningWatcherOnDispose()
    {
        // arrange
        var fetchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloadErrors = 0;

        var client = new FakeSecretsClient(async (call, ct) =>
        {
            if (call == 1)
            {
                return FirstSecret;
            }

            fetchEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return SecondSecret;
        });

        Task? worker = null;
        var watcher = new WatcherLifetimeShould.RecordingWatcher
        {
            OnStart = p => worker = Task.Run(p.Reload),
            OnStop = () => worker!.Wait(HangGuard),
        };

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = client,
            ConfigurationWatcher = watcher,
            OnReloadException = _ => Interlocked.Increment(ref reloadErrors),
        };

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();
        await fetchEntered.Task.WaitAsync(HangGuard);

        // act
        await Task.Run(provider.Dispose).WaitAsync(HangGuard);
        await worker!.WaitAsync(HangGuard);

        // assert
        await Assert.That(reloadErrors).IsEqualTo(0);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(provider.CurrentVersionId).IsEqualTo(FirstSecret.VersionId);
    }

    [Test]
    public async Task ThrowObjectDisposedWithoutInnerExceptionWhenDisposedDuringLoad()
    {
        // arrange
        var fetchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadErrors = 0;

        var client = new FakeSecretsClient(async (_, ct) =>
        {
            fetchEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return FirstSecret;
        });

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = client,
            OnLoadException = _ => Interlocked.Increment(ref loadErrors),
        };

        var provider = new SecretsManagerConfigurationProvider(source);
        var loadTask = Task.Run(provider.Load);
        await fetchEntered.Task.WaitAsync(HangGuard);

        // act
        provider.Dispose();
        var ex = await Assert.That(async () => await loadTask.WaitAsync(HangGuard)).ThrowsExactly<ObjectDisposedException>();

        // assert
        await Assert.That(ex!.InnerException).IsNull();
        await Assert.That(loadErrors).IsEqualTo(0);
        await Assert.That(provider.CurrentVersionId).IsNull();
    }

    [Test]
    public async Task ResetReloadGuardWhenLoggerCreationThrows()
    {
        // arrange
        var loggerFactory = ILoggerFactory.Mock();
        loggerFactory
            .CreateLogger(Any<string>())
            .Returns(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
            .Then()
            .Throws(new ObjectDisposedException("LoggerFactory"))
            .Then()
            .Returns(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var client = new FakeSecretsClient((call, _) => Task.FromResult(call == 1 ? FirstSecret : SecondSecret));
        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = client,
            LoggerFactory = loggerFactory.Object,
        };

        using var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        // act
        await Assert.That(() => provider.Reload()).ThrowsExactly<SecretRetrievalException>();
        provider.Reload();

        // assert
        await Assert.That(provider.CurrentVersionId).IsEqualTo(SecondSecret.VersionId);
    }

    private sealed class FakeSecretsClient : AmazonSecretsManagerClient
    {
        private readonly Func<int, CancellationToken, Task<GetSecretValueResponse>> _handler;
        private int _calls;

        public FakeSecretsClient(Func<int, CancellationToken, Task<GetSecretValueResponse>> handler)
            : base(new AnonymousAWSCredentials(), new AmazonSecretsManagerConfig { RegionEndpoint = RegionEndpoint.USEast1 })
        {
            _handler = handler;
        }

        public int Calls => Volatile.Read(ref _calls);

        public override Task<GetSecretValueResponse> GetSecretValueAsync(
            GetSecretValueRequest request,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            return _handler(call, cancellationToken);
        }
    }

    private sealed class TrackingTimeProvider : TimeProvider
    {
        private int _timerDisposeCalls;

        public FakeTimeProvider Inner { get; } = new(DateTimeOffset.UtcNow);

        public int TimerDisposeCalls => Volatile.Read(ref _timerDisposeCalls);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new TrackingTimer(Inner.CreateTimer(callback, state, dueTime, period), () => Interlocked.Increment(ref _timerDisposeCalls));

        private sealed class TrackingTimer(ITimer inner, Action onDispose) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

            public void Dispose()
            {
                onDispose();
                inner.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                onDispose();
                return inner.DisposeAsync();
            }
        }
    }
}