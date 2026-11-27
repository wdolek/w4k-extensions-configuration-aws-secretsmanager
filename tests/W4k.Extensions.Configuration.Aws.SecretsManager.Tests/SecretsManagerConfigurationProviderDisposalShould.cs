using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Amazon;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretsManagerConfigurationProviderDisposalShould
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private const string FirstVersion = "d6d1b757d46d449d1835a10869dfb9d1";
    private const string SecondVersion = "d6d1b757d46d449d1835a10869dfb9d2";
    private const string ThirdVersion = "d6d1b757d46d449d1835a10869dfb9d3";

    // listeners are process-global; a listener's hooks only act on activities started within the async flow of the test that created it
    private static readonly AsyncLocal<object?> ListenerScope = new();

    private static GetSecretValueResponse FirstSecret => new()
    {
        VersionId = FirstVersion,
        SecretString = """{ "Key": "one" }""",
    };

    private static GetSecretValueResponse SecondSecret => new()
    {
        VersionId = SecondVersion,
        SecretString = """{ "Key": "two" }""",
    };

    private static GetSecretValueResponse ThirdSecret => new()
    {
        VersionId = ThirdVersion,
        SecretString = """{ "Key": "three" }""",
    };

    #region Polling stops when configuration is disposed

    [Test]
    public async Task StopPollingWhenConfigurationRootIsDisposed()
    {
        // arrange
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new FakeSecretsClient((_, _) => Task.FromResult(FirstSecret));

        var root = new ConfigurationBuilder()
            .AddSecretsManager(NewSecretName(), b => b.WithSecretsManager(client).WithPollingWatcher(Interval, time))
            .Build();

        // polling works while the configuration is alive
        time.Advance(Interval + TimeSpan.FromSeconds(1));
        var callsBeforeDispose = client.Calls;
        await Assert.That(callsBeforeDispose).IsEqualTo(2);

        // act
        ((IDisposable)root).Dispose();
        time.Advance(Interval * 3);

        // assert
        await Assert.That(client.Calls).IsEqualTo(callsBeforeDispose);
    }

    [Test]
    public async Task StopPollingWhenConfigurationManagerIsDisposed()
    {
        // arrange
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new FakeSecretsClient((_, _) => Task.FromResult(FirstSecret));

        var manager = new ConfigurationManager();
        manager.AddSecretsManager(NewSecretName(), (_, b) => b.WithSecretsManager(client).WithPollingWatcher(Interval, time));

        time.Advance(Interval + TimeSpan.FromSeconds(1));
        var callsBeforeDispose = client.Calls;
        await Assert.That(callsBeforeDispose).IsEqualTo(2);

        // act
        manager.Dispose();
        time.Advance(Interval * 3);

        // assert
        await Assert.That(client.Calls).IsEqualTo(callsBeforeDispose);
    }

    #endregion

    #region Watcher ownership

    [Test]
    public async Task DisposePollingWatcherCreatedWithInterval() =>
        await AssertPollingWatcherIsLibraryOwned(b => b.WithPollingWatcher(Interval));

    [Test]
    public async Task DisposePollingWatcherCreatedWithIntervalAndTimeProvider() =>
        await AssertPollingWatcherIsLibraryOwned(b => b.WithPollingWatcher(Interval, new FakeTimeProvider()));

    [Test]
    public async Task DisposePollingWatcherCreatedWithIntervalAndJitter() =>
        await AssertPollingWatcherIsLibraryOwned(b => b.WithPollingWatcher(Interval, TimeSpan.FromSeconds(5)));

    [Test]
    public async Task DisposePollingWatcherCreatedWithIntervalJitterAndTimeProvider() =>
        await AssertPollingWatcherIsLibraryOwned(b => b.WithPollingWatcher(Interval, TimeSpan.FromSeconds(5), new FakeTimeProvider()));

    [Test]
    public async Task StopButNotDisposeWatcherSetWithConfigurationWatcher()
    {
        // arrange
        var watcher = new RecordingWatcher();
        var source = new SecretsManagerConfigurationBuilder(NewSecretName())
            .WithSecretsManager(ConstantClient(FirstSecret))
            .WithConfigurationWatcher(watcher)
            .Build();

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        // act
        provider.Dispose();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task StopButNotDisposeWatcherSetWithProperty()
    {
        // arrange
        var watcher = new RecordingWatcher();
        var source = NewSource(ConstantClient(FirstSecret));
        source.ConfigurationWatcher = watcher;

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        // act
        provider.Dispose();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task StopButNotDisposeWatcherSetWithPropertyAfterPollingWatcher()
    {
        // arrange
        var watcher = new RecordingWatcher();
        var source = new SecretsManagerConfigurationBuilder(NewSecretName())
            .WithSecretsManager(ConstantClient(FirstSecret))
            .WithPollingWatcher(Interval, new FakeTimeProvider())
            .Build();

        source.ConfigurationWatcher = watcher;

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        // act
        provider.Dispose();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task StopButNotDisposeWatcherSetViaSourceAction()
    {
        // arrange
        var watcher = new RecordingWatcher();
        var client = ConstantClient(FirstSecret);

        var root = new ConfigurationBuilder()
            .AddSecretsManager(source =>
            {
                source.SecretName = NewSecretName();
                source.SecretsManager = client;
                source.ConfigurationWatcher = watcher;
            })
            .Build();

        // act
        ((IDisposable)root).Dispose();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task NotChangeWatcherWhenSourceWatcherIsReplacedAfterProviderIsCreated()
    {
        // arrange
        var original = new RecordingWatcher();
        var replacement = new RecordingWatcher();

        var source = NewSource(ConstantClient(FirstSecret));
        source.ConfigurationWatcher = original;

        var provider = new SecretsManagerConfigurationProvider(source);
        source.ConfigurationWatcher = replacement;

        // act
        provider.Load();
        provider.Dispose();

        // assert
        await Assert.That(original.StartCalls).IsEqualTo(1);
        await Assert.That(original.StopCalls).IsEqualTo(1);
        await Assert.That(replacement.StartCalls).IsEqualTo(0);
        await Assert.That(replacement.StopCalls).IsEqualTo(0);
    }

    [Test]
    public async Task DisposePreviousPollingWatcherWhenReplacedByConfigurationWatcher()
    {
        // arrange
        var customWatcher = new RecordingWatcher();
        var builder = new SecretsManagerConfigurationBuilder(NewSecretName())
            .WithSecretsManager(ConstantClient(FirstSecret))
            .WithPollingWatcher(Interval, new FakeTimeProvider());

        var pollingWatcher = builder.Build().ConfigurationWatcher as SecretsManagerPollingWatcher;
        await Assert.That(pollingWatcher).IsNotNull();

        // act
        var source = builder.WithConfigurationWatcher(customWatcher).Build();
        var provider = new SecretsManagerConfigurationProvider(source);

        // assert: overwritten library-owned watcher was already disposed before provider start/disposal
        await Assert.That(() => pollingWatcher!.StartWatching(provider)).ThrowsExactly<ObjectDisposedException>();

        provider.Load();
        provider.Dispose();

        await Assert.That(customWatcher.StartCalls).IsEqualTo(1);
        await Assert.That(customWatcher.StopCalls).IsEqualTo(1);
        await Assert.That(customWatcher.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task DisposePreviousPollingWatcherWhenReplacedByAnotherPollingWatcher()
    {
        // arrange
        var builder = new SecretsManagerConfigurationBuilder(NewSecretName())
            .WithSecretsManager(ConstantClient(FirstSecret))
            .WithPollingWatcher(Interval, new FakeTimeProvider());

        var firstWatcher = builder.Build().ConfigurationWatcher as SecretsManagerPollingWatcher;
        await Assert.That(firstWatcher).IsNotNull();

        // act
        var source = builder.WithPollingWatcher(Interval * 2, new FakeTimeProvider()).Build();
        var secondWatcher = source.ConfigurationWatcher as SecretsManagerPollingWatcher;
        await Assert.That(secondWatcher).IsNotNull();
        await Assert.That(ReferenceEquals(firstWatcher, secondWatcher)).IsFalse();

        var provider = new SecretsManagerConfigurationProvider(source);

        // assert: first watcher disposed on overwrite, second stays alive until provider disposes
        await Assert.That(() => firstWatcher!.StartWatching(provider)).ThrowsExactly<ObjectDisposedException>();

        provider.Load();
        provider.Dispose();

        await Assert.That(() => secondWatcher!.StartWatching(provider)).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task DisposePreviousPollingWatcherWhenClearedViaPropertyBeforeProviderCreated()
    {
        // arrange
        var source = new SecretsManagerConfigurationBuilder(NewSecretName())
            .WithSecretsManager(ConstantClient(FirstSecret))
            .WithPollingWatcher(Interval, new FakeTimeProvider())
            .Build();

        var pollingWatcher = source.ConfigurationWatcher as SecretsManagerPollingWatcher;
        await Assert.That(pollingWatcher).IsNotNull();

        // act
        source.ConfigurationWatcher = null;
        var provider = new SecretsManagerConfigurationProvider(source);

        // assert
        await Assert.That(() => pollingWatcher!.StartWatching(provider)).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task NotDisposePollingWatcherWhenSourceWatcherIsReplacedAfterProviderIsCreated()
    {
        // arrange
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = ConstantClient(FirstSecret);
        var replacement = new RecordingWatcher();

        var source = new SecretsManagerConfigurationBuilder(NewSecretName())
            .WithSecretsManager(client)
            .WithPollingWatcher(Interval, time)
            .Build();

        var pollingWatcher = source.ConfigurationWatcher as SecretsManagerPollingWatcher;
        await Assert.That(pollingWatcher).IsNotNull();

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        // act: replacing source.ConfigurationWatcher after provider creation must not dispose the active watcher
        source.ConfigurationWatcher = replacement;

        time.Advance(Interval + TimeSpan.FromSeconds(1));
        await Assert.That(client.Calls).IsEqualTo(2);

        provider.Dispose();

        // assert
        await Assert.That(() => pollingWatcher!.StartWatching(provider)).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(replacement.StartCalls).IsEqualTo(0);
        await Assert.That(replacement.StopCalls).IsEqualTo(0);
        await Assert.That(replacement.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task NotDisposeSameWatcherWhenOwnershipFlipsToCaller()
    {
        // arrange
        var watcher = new RecordingWatcher();
        var source = NewSource(ConstantClient(FirstSecret));
        source.SetConfigurationWatcher(watcher, isOwned: true);

        // act: assigning the same instance flips ownership without disposing it
        source.ConfigurationWatcher = watcher;

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();
        provider.Dispose();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(0);
    }

    #endregion

    #region Watcher lifecycle

    [Test]
    public async Task NotStopWatcherThatWasNeverStarted()
    {
        // arrange
        var callerOwned = new RecordingWatcher();
        var libraryOwned = new RecordingWatcher();

        var callerOwnedProvider = CreateProviderWithFailingLoad(source => source.ConfigurationWatcher = callerOwned);
        var libraryOwnedProvider = CreateProviderWithFailingLoad(source => source.SetConfigurationWatcher(libraryOwned, isOwned: true));

        // act
        callerOwnedProvider.Dispose();
        libraryOwnedProvider.Dispose();

        // assert
        await Assert.That(callerOwned.StartCalls).IsEqualTo(0);
        await Assert.That(callerOwned.StopCalls).IsEqualTo(0);
        await Assert.That(callerOwned.DisposeCalls).IsEqualTo(0);

        await Assert.That(libraryOwned.StartCalls).IsEqualTo(0);
        await Assert.That(libraryOwned.StopCalls).IsEqualTo(0);
        await Assert.That(libraryOwned.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task StartWatcherOnceWhenInitialLoadWasIgnoredAndLaterLoadSucceeds()
    {
        // arrange
        var watcher = new RecordingWatcher();
        var client = new FakeSecretsClient((call, _) => call == 1
            ? throw new InvalidOperationException("first load fails")
            : Task.FromResult(FirstSecret));

        var source = NewSource(client);
        source.ConfigurationWatcher = watcher;
        source.OnLoadException = ctx => ctx.Ignore = true;

        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        provider.Load();
        await Assert.That(watcher.StartCalls).IsEqualTo(0);

        provider.Load();
        provider.Load();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
    }

    [Test]
    public async Task StartWatcherOnceWhenRootIsReloaded()
    {
        // arrange
        var watcher = new RecordingWatcher();
        var client = ConstantClient(FirstSecret);

        var root = new ConfigurationBuilder()
            .AddSecretsManager(NewSecretName(), b => b.WithSecretsManager(client).WithConfigurationWatcher(watcher))
            .Build();

        // act
        root.Reload();

        // assert
        await Assert.That(watcher.StartCalls).IsEqualTo(1);
        await Assert.That(root["Key"]).IsEqualTo("one");
    }

    [Test]
    public async Task TryToStartWatcherAgainWhenStartFails()
    {
        // arrange
        var starts = 0;
        var watcher = new RecordingWatcher
        {
            OnStart = _ =>
            {
                if (Interlocked.Increment(ref starts) == 1)
                {
                    throw new InvalidOperationException("start failed");
                }
            },
        };

        var source = NewSource(ConstantClient(FirstSecret));
        source.ConfigurationWatcher = watcher;

        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        var ex = await Assert.That(() => provider.Load()).ThrowsExactly<SecretRetrievalException>();
        await Assert.That(ex!.InnerException).IsTypeOf<InvalidOperationException>();

        provider.Load();
        provider.Load();
        provider.Dispose();

        // assert: failed start is not stopped, the successful one is
        await Assert.That(watcher.StartCalls).IsEqualTo(2);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
    }

    [Test]
    public async Task StopAndDisposeWatcherOnlyOnceWhenDisposedTwice()
    {
        // arrange
        var watcher = new RecordingWatcher();
        var source = NewSource(ConstantClient(FirstSecret));
        source.SetConfigurationWatcher(watcher, isOwned: true);

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        // act
        provider.Dispose();
        provider.Dispose();

        // assert
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task PropagateStopFailureAfterCancellingFetch()
    {
        // arrange
        var fetchStarted = NewGate();
        var releaseFetch = NewGate();
        var fetchToken = CancellationToken.None;

        var client = new FakeSecretsClient(async (call, ct) =>
        {
            if (call == 1)
            {
                return FirstSecret;
            }

            fetchToken = ct;
            fetchStarted.TrySetResult();

            // deliberately ignores cancellation
            await releaseFetch.Task.WaitAsync(HangGuard);
            return FirstSecret;
        });

        var tokenCancelledWhenStopping = false;
        var watcher = new RecordingWatcher
        {
            OnStop = () =>
            {
                tokenCancelledWhenStopping = fetchToken.IsCancellationRequested;
                throw new InvalidOperationException("stop failed");
            },
        };

        var source = NewSource(client);
        source.ConfigurationWatcher = watcher;

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        var reload = Task.Run(provider.Reload);
        await fetchStarted.Task.WaitAsync(HangGuard);

        // act
        var ex = await Assert.That(() => provider.Dispose()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => provider.Dispose()).ThrowsNothing();

        releaseFetch.SetResult();
        var reloadFailure = await ObserveAsync(reload);

        // assert
        await Assert.That(ex!.Message).IsEqualTo("stop failed");
        await Assert.That(tokenCancelledWhenStopping).IsTrue();
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(reloadFailure).IsNull();
    }

    #endregion

    #region Use after disposal

    [Test]
    public async Task IgnoreReloadAfterDispose()
    {
        // arrange
        var reloadFailures = 0;
        var client = ConstantClient(FirstSecret);
        var source = NewSource(client);
        source.OnReloadException = _ => reloadFailures++;

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        var reloadToken = provider.GetReloadToken();
        var callbackCalls = 0;
        _ = reloadToken.RegisterChangeCallback(_ => callbackCalls++, null);

        provider.Dispose();
        var callsBeforeReload = client.Calls;

        // act
        await Assert.That(() => provider.Reload()).ThrowsNothing();

        // assert
        await Assert.That(client.Calls).IsEqualTo(callsBeforeReload);
        await Assert.That(reloadToken.HasChanged).IsFalse();
        await Assert.That(callbackCalls).IsEqualTo(0);
        await Assert.That(reloadFailures).IsEqualTo(0);
    }

    [Test]
    public async Task ThrowOnLoadAfterDispose()
    {
        // arrange
        var client = ConstantClient(FirstSecret);
        var provider = new SecretsManagerConfigurationProvider(NewSource(client));
        provider.Load();
        provider.Dispose();

        var callsBeforeLoad = client.Calls;

        // act & assert
        await Assert.That(() => provider.Load()).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(client.Calls).IsEqualTo(callsBeforeLoad);
    }

    #endregion

    #region Races with the watcher start

    [Test]
    public async Task StopWatcherAfterStartReturnsWhenDisposedWhileStarting()
    {
        // arrange
        var entered = NewGate();
        var release = NewGate();
        var watcher = new RecordingWatcher
        {
            OnStart = _ =>
            {
                entered.TrySetResult();
                release.Task.Wait(HangGuard);
            },
        };

        var source = NewSource(ConstantClient(FirstSecret));
        source.ConfigurationWatcher = watcher;
        var provider = new SecretsManagerConfigurationProvider(source);

        var load = Task.Run(provider.Load);
        await entered.Task.WaitAsync(HangGuard);

        // act
        var dispose = Task.Run(provider.Dispose);
        release.SetResult();

        var loadFailure = await ObserveAsync(load);
        await dispose.WaitAsync(HangGuard);

        // assert
        await Assert.That(loadFailure).IsNull();
        await Assert.That(provider.CurrentVersionId).IsEqualTo(FirstVersion);
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.StopSawStartReturned).IsTrue();
    }

    [Test]
    public async Task NotStopWatcherWhenStartFailsWhileDisposing()
    {
        // arrange
        var entered = NewGate();
        var release = NewGate();
        var watcher = new RecordingWatcher
        {
            OnStart = _ =>
            {
                entered.TrySetResult();
                release.Task.Wait(HangGuard);
                throw new InvalidOperationException("start failed");
            },
        };

        var source = NewSource(ConstantClient(FirstSecret));
        source.SetConfigurationWatcher(watcher, isOwned: true);
        var provider = new SecretsManagerConfigurationProvider(source);

        var load = Task.Run(provider.Load);
        await entered.Task.WaitAsync(HangGuard);

        // act
        var dispose = Task.Run(provider.Dispose);
        release.SetResult();

        var loadFailure = await ObserveAsync(load);
        await dispose.WaitAsync(HangGuard);

        // assert
        if (loadFailure is not null)
        {
            await Assert.That(loadFailure).IsTypeOf<SecretRetrievalException>();
        }

        await Assert.That(watcher.StopCalls).IsEqualTo(0);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(1);
        await Assert.That(() => provider.Load()).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task NotStartCallerOwnedWatcherWhenConcurrentDisposeIsInProgressBeforeStart() =>
        await AssertWatcherNotStartedWhenConcurrentDisposeIsInProgress(libraryOwned: false);

    [Test]
    public async Task NotStartAndStillDisposeLibraryOwnedWatcherWhenConcurrentDisposeIsInProgressBeforeStart() =>
        await AssertWatcherNotStartedWhenConcurrentDisposeIsInProgress(libraryOwned: true);

    [Test]
    public async Task CompleteDisposeWhenStopWaitsForWorkerBlockedInFetch()
    {
        // arrange
        var startReload = NewGate();
        var fetchEntered = NewGate();

        var client = new FakeSecretsClient(async (call, ct) =>
        {
            if (call == 1)
            {
                return FirstSecret;
            }

            fetchEntered.TrySetResult();

            // completes only when cancelled
            var cancelled = NewGate();
            await using var registration = ct.Register(() => cancelled.TrySetResult());
            await cancelled.Task;

            ct.ThrowIfCancellationRequested();
            return FirstSecret;
        });

        Task? worker = null;
        var stopJoinedWorker = false;
        var watcher = new RecordingWatcher
        {
            OnStart = p => worker = Task.Run(async () =>
            {
                await startReload.Task;
                p.Reload();
            }),
            OnStop = () => stopJoinedWorker = worker!.Wait(HangGuard),
        };

        var source = NewSource(client);
        source.ConfigurationWatcher = watcher;
        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        startReload.SetResult();
        await fetchEntered.Task.WaitAsync(HangGuard);

        // act
        await Task.Run(provider.Dispose).WaitAsync(HangGuard);

        // assert
        await Assert.That(stopJoinedWorker).IsTrue();
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
    }

    [Test]
    public async Task StopCallerOwnedWatcherOnceWhenCancellationRegistrationThrowsDuringDispose() =>
        await AssertWatcherReleasedWhenCancellationRegistrationThrows(libraryOwned: false);

    [Test]
    public async Task StopAndDisposeLibraryOwnedWatcherOnceWhenCancellationRegistrationThrowsDuringDispose() =>
        await AssertWatcherReleasedWhenCancellationRegistrationThrows(libraryOwned: true);

    #endregion

    #region Disposal during reload

    [Test]
    public async Task PublishDataWithoutDeadlockWhenReloadTokenCallbackDisposesProvider()
    {
        // arrange
        var secretName = NewSecretName();
        using var metrics = new MetricsCollector();
        var loggers = new RecordingLoggerFactory();

        var client = new FakeSecretsClient((call, _) => Task.FromResult(call == 1 ? FirstSecret : SecondSecret));
        var source = NewSource(client, secretName);
        source.LoggerFactory = loggers;

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        _ = provider.GetReloadToken().RegisterChangeCallback(
            _ =>
            {
                loggers.ThrowOnLog = true;
                provider.Dispose();
            },
            null);

        // act
        await Assert.That(() => provider.Reload()).ThrowsNothing();

        // assert
        await Assert.That(provider.CurrentVersionId).IsEqualTo(SecondVersion);
        await Assert.That(provider.TryGet("Key", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("two");
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.reloads.failed", secretName)).IsEqualTo(0);
    }

    [Test]
    public async Task IgnoreCancelledFetchOfReloadInFlightDuringDispose()
    {
        // arrange
        var secretName = NewSecretName();
        using var metrics = new MetricsCollector();
        using var activityListener = StartActivityListener(out var activities);
        var loggers = new RecordingLoggerFactory();

        var cancellationObserved = NewGate();
        var fetchStarted = NewGate();
        var releaseFetch = NewGate();

        var client = new FakeSecretsClient(async (call, ct) =>
        {
            if (call == 1)
            {
                return FirstSecret;
            }

            await using var registration = ct.Register(() => cancellationObserved.TrySetResult());
            fetchStarted.TrySetResult();

            await releaseFetch.Task.WaitAsync(HangGuard);
            throw new OperationCanceledException(ct);
        });

        var reloadFailures = 0;
        var source = NewSource(client, secretName);
        source.LoggerFactory = loggers;
        source.OnReloadException = _ => Interlocked.Increment(ref reloadFailures);

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        var reloadToken = provider.GetReloadToken();
        var reload = Task.Run(provider.Reload);
        await fetchStarted.Task.WaitAsync(HangGuard);

        // act
        await Task.Run(provider.Dispose).WaitAsync(HangGuard);

        // assert: Dispose does not wait for the fetch, which has observed the cancellation but is still held
        await Assert.That(cancellationObserved.Task.IsCompletedSuccessfully).IsTrue();
        await Assert.That(reload.IsCompleted).IsFalse();

        releaseFetch.SetResult();
        var reloadFailure = await ObserveAsync(reload);

        await Assert.That(reloadFailure).IsNull();
        await Assert.That(provider.CurrentVersionId).IsEqualTo(FirstVersion);
        await Assert.That(provider.TryGet("Key", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("one");
        await Assert.That(reloadToken.HasChanged).IsFalse();
        await Assert.That(reloadFailures).IsEqualTo(0);
        await Assert.That(loggers.Events).DoesNotContain("FailedToReloadSecret");
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.reloads.failed", secretName)).IsEqualTo(0);

        var activity = FindActivity(activities, ActivityDescriptors.ReloadActivityName, secretName);
        await Assert.That(activity).IsNotNull();
        await Assert.That(activity!.Events.Any(e => e.Name == "disposed")).IsTrue();
    }

    [Test]
    public async Task NotPublishWhenFetchIgnoresCancellationAndSucceedsAfterDispose()
    {
        // arrange
        var fetchStarted = NewGate();
        var releaseFetch = NewGate();

        var client = new FakeSecretsClient(async (call, _) =>
        {
            if (call == 1)
            {
                return FirstSecret;
            }

            fetchStarted.TrySetResult();
            await releaseFetch.Task.WaitAsync(HangGuard);
            return SecondSecret;
        });

        var provider = new SecretsManagerConfigurationProvider(NewSource(client));
        provider.Load();

        var reloadToken = provider.GetReloadToken();
        var reload = Task.Run(provider.Reload);
        await fetchStarted.Task.WaitAsync(HangGuard);

        // act
        await Task.Run(provider.Dispose).WaitAsync(HangGuard);
        releaseFetch.SetResult();
        var reloadFailure = await ObserveAsync(reload);

        // assert
        await Assert.That(reloadFailure).IsNull();
        await Assert.That(provider.CurrentVersionId).IsEqualTo(FirstVersion);
        await Assert.That(provider.TryGet("Key", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("one");
        await Assert.That(reloadToken.HasChanged).IsFalse();
    }

    [Test]
    public async Task SwallowNonCancellationFailureOfFetchAfterDispose()
    {
        // arrange
        var fetchStarted = NewGate();
        var releaseFetch = NewGate();

        var client = new FakeSecretsClient(async (call, _) =>
        {
            if (call == 1)
            {
                return FirstSecret;
            }

            fetchStarted.TrySetResult();
            await releaseFetch.Task.WaitAsync(HangGuard);
            throw new InvalidOperationException("client is disposed");
        });

        var reloadFailures = 0;
        var source = NewSource(client);
        source.OnReloadException = _ => Interlocked.Increment(ref reloadFailures);

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        var reload = Task.Run(provider.Reload);
        await fetchStarted.Task.WaitAsync(HangGuard);

        // act
        await Task.Run(provider.Dispose).WaitAsync(HangGuard);
        releaseFetch.SetResult();
        var reloadFailure = await ObserveAsync(reload);

        // assert
        await Assert.That(reloadFailure).IsNull();
        await Assert.That(reloadFailures).IsEqualTo(0);
        await Assert.That(provider.CurrentVersionId).IsEqualTo(FirstVersion);
    }

    [Test]
    public async Task NotPublishReloadWhenProcessorDisposesProvider()
    {
        // arrange
        var reloadFailures = 0;
        var client = new FakeSecretsClient((call, _) => Task.FromResult(call == 1 ? FirstSecret : SecondSecret));
        var source = NewSource(client);
        source.OnReloadException = _ => Interlocked.Increment(ref reloadFailures);

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        var reloadToken = provider.GetReloadToken();
        source.Processor = new CallbackProcessor(provider.Dispose);

        // act
        await Assert.That(() => provider.Reload()).ThrowsNothing();

        // assert
        await Assert.That(provider.CurrentVersionId).IsEqualTo(FirstVersion);
        await Assert.That(provider.TryGet("Key", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("one");
        await Assert.That(reloadToken.HasChanged).IsFalse();
        await Assert.That(reloadFailures).IsEqualTo(0);
    }

    #endregion

    #region Disposal during load

    [Test]
    public async Task NotPublishLoadWhenProcessorDisposesProvider()
    {
        // arrange
        var loadFailures = 0;
        var source = NewSource(ConstantClient(FirstSecret));
        source.OnLoadException = _ => Interlocked.Increment(ref loadFailures);

        var provider = new SecretsManagerConfigurationProvider(source);
        source.Processor = new CallbackProcessor(provider.Dispose);

        // act
        var ex = await Assert.That(() => provider.Load()).ThrowsExactly<ObjectDisposedException>();

        // assert
        await Assert.That(ex!.InnerException).IsNull();
        await Assert.That(provider.CurrentVersionId).IsNull();
        await Assert.That(provider.TryGet("Key", out _)).IsFalse();
        await Assert.That(loadFailures).IsEqualTo(0);
    }

    [Test]
    public async Task ThrowObjectDisposedWithoutInnerExceptionWhenLoadInFlightDuringDispose()
    {
        // arrange
        var secretName = NewSecretName();
        using var metrics = new MetricsCollector();

        var fetchStarted = NewGate();
        var releaseFetch = NewGate();

        var client = new FakeSecretsClient(async (_, _) =>
        {
            fetchStarted.TrySetResult();
            await releaseFetch.Task.WaitAsync(HangGuard);
            throw new InvalidOperationException("secret detail that must not leak");
        });

        var loadFailures = 0;
        var source = NewSource(client, secretName);
        source.OnLoadException = _ => Interlocked.Increment(ref loadFailures);

        var provider = new SecretsManagerConfigurationProvider(source);
        var load = Task.Run(provider.Load);
        await fetchStarted.Task.WaitAsync(HangGuard);

        // act
        await Task.Run(provider.Dispose).WaitAsync(HangGuard);
        releaseFetch.SetResult();
        var loadFailure = await ObserveAsync(load);

        // assert
        await Assert.That(loadFailure).IsTypeOf<ObjectDisposedException>();
        await Assert.That(loadFailure!.InnerException).IsNull();
        await Assert.That(provider.CurrentVersionId).IsNull();
        await Assert.That(provider.TryGet("Key", out _)).IsFalse();
        await Assert.That(loadFailures).IsEqualTo(0);
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.loads.failed", secretName)).IsEqualTo(0);
    }

    [Test]
    public async Task ReturnNormallyWhenChangeNotificationDisposesProviderAndLoggerThrowsDuringLoad()
    {
        // arrange
        var secretName = NewSecretName();
        using var activityListener = StartActivityListener(out var activities);
        var loggers = new RecordingLoggerFactory { ThrowOnLog = true };
        var watcher = new RecordingWatcher();

        var source = NewSource(ConstantClient(FirstSecret), secretName);
        source.LoggerFactory = loggers;
        source.ConfigurationWatcher = watcher;

        var provider = new SecretsManagerConfigurationProvider(source);
        _ = provider.GetReloadToken().RegisterChangeCallback(_ => provider.Dispose(), null);

        // act
        await Assert.That(() => provider.Load()).ThrowsNothing();

        // assert
        await Assert.That(provider.CurrentVersionId).IsEqualTo(FirstVersion);
        await Assert.That(provider.TryGet("Key", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("one");
        await Assert.That(watcher.StartCalls).IsEqualTo(0);
        await Assert.That(watcher.StopCalls).IsEqualTo(0);

        var activity = FindActivity(activities, ActivityDescriptors.LoadActivityName, secretName);
        await Assert.That(activity).IsNotNull();
        await Assert.That(activity!.Events.Any(e => e.Name == "disposed")).IsTrue();
    }

    [Test]
    public async Task ReturnNormallyFromPublishedLoadWhenLoggerDisposesProviderAndThrows()
    {
        // arrange
        SecretsManagerConfigurationProvider? provider = null;
        var loggers = new RecordingLoggerFactory
        {
            ThrowOnLog = true,
            OnLog = eventId =>
            {
                if (eventId.Name == "SecretLoaded")
                {
                    provider!.Dispose();
                }
            },
        };

        var watcher = new RecordingWatcher();
        var source = NewSource(ConstantClient(FirstSecret));
        source.LoggerFactory = loggers;
        source.ConfigurationWatcher = watcher;

        provider = new SecretsManagerConfigurationProvider(source);

        // act
        await Assert.That(() => provider.Load()).ThrowsNothing();

        // assert
        await Assert.That(loggers.Events).Contains("SecretLoaded");
        await Assert.That(provider.CurrentVersionId).IsEqualTo(FirstVersion);
        await Assert.That(provider.TryGet("Key", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("one");
        await Assert.That(watcher.StartCalls).IsEqualTo(0);
        await Assert.That(watcher.StopCalls).IsEqualTo(0);
    }

    #endregion

    #region Reload guard

    [Test]
    public async Task ResetReloadGuardWhenLoggerCreationFails()
    {
        // arrange
        var loggers = new RecordingLoggerFactory();
        var client = new FakeSecretsClient((call, _) => Task.FromResult(call == 1 ? FirstSecret : SecondSecret));

        var source = NewSource(client);
        source.LoggerFactory = loggers;

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        // act
        loggers.ThrowOnCreateLogger = true;
        var ex = await Assert.That(() => provider.Reload()).ThrowsExactly<SecretRetrievalException>();
        await Assert.That(ex!.InnerException).IsTypeOf<InvalidOperationException>();
        await Assert.That(client.Calls).IsEqualTo(1);

        loggers.ThrowOnCreateLogger = false;
        provider.Reload();

        // assert
        await Assert.That(client.Calls).IsEqualTo(2);
        await Assert.That(provider.CurrentVersionId).IsEqualTo(SecondVersion);
    }

    [Test]
    public async Task ResetReloadGuardWhenActivityStoppedListenerFails()
    {
        // arrange
        ListenerScope.Value = new object();

        var client = new FakeSecretsClient((call, _) => Task.FromResult(call switch
        {
            1 => FirstSecret,
            2 => SecondSecret,
            _ => ThirdSecret,
        }));

        var provider = new SecretsManagerConfigurationProvider(NewSource(client));
        provider.Load();

        var stops = 0;
        using var listener = StartActivityListener(
            out _,
            onStopped: activity =>
            {
                if (activity.OperationName == ActivityDescriptors.ReloadActivityName && Interlocked.Increment(ref stops) == 1)
                {
                    throw new InvalidOperationException("listener failed");
                }
            });

        // act: provider is not disposed, the listener failure surfaces as before
        await Assert.That(() => provider.Reload()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(GetReloadGuard(provider)).IsEqualTo(0);

        provider.Reload();

        // assert
        await Assert.That(client.Calls).IsEqualTo(3);
        await Assert.That(provider.CurrentVersionId).IsEqualTo(ThirdVersion);
    }

    #endregion

    #region Fetch timeout

    [Test]
    public async Task FailLoadWhenTimeoutIsTooLargeForCancelAfter()
    {
        // arrange
        var client = ConstantClient(FirstSecret);
        var source = NewSource(client);
        source.Timeout = TimeSpan.FromDays(60);

        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        var first = await Assert.That(() => provider.Load()).ThrowsExactly<SecretRetrievalException>();
        var second = await Assert.That(() => provider.Load()).ThrowsExactly<SecretRetrievalException>();

        // assert
        await Assert.That(first!.InnerException).IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(second!.InnerException).IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(client.Calls).IsEqualTo(0);
        await Assert.That(() => provider.Dispose()).ThrowsNothing();
    }

    #endregion

    private static async Task AssertPollingWatcherIsLibraryOwned(
        Func<SecretsManagerConfigurationBuilder, SecretsManagerConfigurationBuilder> configure)
    {
        // arrange
        var builder = new SecretsManagerConfigurationBuilder(NewSecretName())
            .WithSecretsManager(ConstantClient(FirstSecret));

        var source = configure(builder).Build();
        var watcher = source.ConfigurationWatcher as SecretsManagerPollingWatcher;
        await Assert.That(watcher).IsNotNull();

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        // act
        provider.Dispose();

        // assert
        await Assert.That(() => watcher!.StartWatching(provider)).ThrowsExactly<ObjectDisposedException>();
    }

    private static async Task AssertWatcherNotStartedWhenConcurrentDisposeIsInProgress(bool libraryOwned)
    {
        // arrange
        var reloadFetchEntered = NewGate();
        var cancelCallbackEntered = NewGate();
        var releaseCancelCallback = NewGate();
        var releaseReloadFetch = NewGate();

        var client = new FakeSecretsClient(async (call, ct) =>
        {
            if (call == 1)
            {
                return FirstSecret;
            }

            await using var registration = ct.Register(() =>
            {
                cancelCallbackEntered.TrySetResult();
                releaseCancelCallback.Task.Wait(HangGuard);
            });

            reloadFetchEntered.TrySetResult();
            await releaseReloadFetch.Task.WaitAsync(HangGuard);
            throw new OperationCanceledException(ct);
        });

        var watcher = new RecordingWatcher();
        var source = NewSource(client);
        if (libraryOwned)
        {
            source.SetConfigurationWatcher(watcher, isOwned: true);
        }
        else
        {
            source.ConfigurationWatcher = watcher;
        }

        var provider = new SecretsManagerConfigurationProvider(source);

        Task? reload = null;
        Task? dispose = null;
        _ = provider.GetReloadToken().RegisterChangeCallback(
            _ =>
            {
                reload = Task.Run(provider.Reload);
                reloadFetchEntered.Task.Wait(HangGuard);

                // pause Dispose() after marking disposed and before ReleaseWatcher()
                dispose = Task.Run(provider.Dispose);
                cancelCallbackEntered.Task.Wait(HangGuard);
            },
            null);

        // act
        await Assert.That(() => provider.Load()).ThrowsNothing();
        await Assert.That(watcher.StartCalls).IsEqualTo(0);

        releaseCancelCallback.SetResult();
        releaseReloadFetch.SetResult();

        var disposeFailure = await ObserveAsync(dispose!);
        var reloadFailure = await ObserveAsync(reload!);

        // assert
        await Assert.That(disposeFailure).IsNull();
        await Assert.That(reloadFailure).IsNull();
        await Assert.That(provider.CurrentVersionId).IsEqualTo(FirstVersion);
        await Assert.That(watcher.StartCalls).IsEqualTo(0);
        await Assert.That(watcher.StopCalls).IsEqualTo(0);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(libraryOwned ? 1 : 0);
    }

    private static async Task AssertWatcherReleasedWhenCancellationRegistrationThrows(bool libraryOwned)
    {
        // arrange
        var fetchStarted = NewGate();
        var releaseFetch = NewGate();

        var client = new FakeSecretsClient(async (call, ct) =>
        {
            if (call == 1)
            {
                return FirstSecret;
            }

            await using var registration = ct.Register(() => throw new InvalidOperationException("boom"));
            fetchStarted.TrySetResult();

            await releaseFetch.Task.WaitAsync(HangGuard);
            throw new OperationCanceledException(ct);
        });

        var watcher = new RecordingWatcher();
        var source = NewSource(client);
        if (libraryOwned)
        {
            source.SetConfigurationWatcher(watcher, isOwned: true);
        }
        else
        {
            source.ConfigurationWatcher = watcher;
        }

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        var reload = Task.Run(provider.Reload);
        await fetchStarted.Task.WaitAsync(HangGuard);

        // act
        var ex = await Assert.That(() => provider.Dispose()).ThrowsExactly<AggregateException>();
        await Assert.That(() => provider.Dispose()).ThrowsNothing();

        releaseFetch.SetResult();
        var reloadFailure = await ObserveAsync(reload);

        // assert
        await Assert.That(ex!.Flatten().InnerExceptions.Any(e => e is InvalidOperationException { Message: "boom" })).IsTrue();
        await Assert.That(watcher.StopCalls).IsEqualTo(1);
        await Assert.That(watcher.DisposeCalls).IsEqualTo(libraryOwned ? 1 : 0);
        await Assert.That(reloadFailure).IsNull();
    }

    private static SecretsManagerConfigurationProvider CreateProviderWithFailingLoad(Action<SecretsManagerConfigurationSource> configureWatcher)
    {
        var client = new FakeSecretsClient((_, _) => throw new InvalidOperationException("load fails"));
        var source = NewSource(client);
        source.OnLoadException = ctx => ctx.Ignore = true;
        configureWatcher(source);

        var provider = new SecretsManagerConfigurationProvider(source);
        provider.Load();

        return provider;
    }

    private static string NewSecretName() => $"le-secret-{Guid.NewGuid():N}";

    private static SecretsManagerConfigurationSource NewSource(IAmazonSecretsManager client, string? secretName = null) =>
        new() { SecretName = secretName ?? NewSecretName(), SecretsManager = client };

    private static FakeSecretsClient ConstantClient(GetSecretValueResponse response) =>
        new((_, _) => Task.FromResult(response));

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<Exception?> ObserveAsync(Task task)
    {
        try
        {
            await task.WaitAsync(HangGuard);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static int GetReloadGuard(SecretsManagerConfigurationProvider provider) =>
        (int)typeof(SecretsManagerConfigurationProvider)
            .GetField("_reloadInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(provider)!;

    private static ActivityListener StartActivityListener(
        out ConcurrentBag<Activity> activities,
        Action<Activity>? onStopped = null)
    {
        var stopped = new ConcurrentBag<Activity>();
        var scope = ListenerScope.Value;

        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivityDescriptors.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                stopped.Add(activity);
                if (scope is not null && ReferenceEquals(ListenerScope.Value, scope))
                {
                    onStopped?.Invoke(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(listener);

        activities = stopped;
        return listener;
    }

    private static Activity? FindActivity(ConcurrentBag<Activity> activities, string operationName, string secretName) =>
        activities.SingleOrDefault(a =>
            a.OperationName == operationName &&
            a.Tags.FirstOrDefault(t => t.Key == "aws.secretsmanager.secret.id").Value == secretName);

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

    private sealed class RecordingWatcher : IConfigurationWatcher, IDisposable
    {
        private int _starts;
        private int _stops;
        private int _disposes;
        private bool _startReturned;
        private bool _stopSawStartReturned;

        public Action<ISecretsManagerConfigurationProvider>? OnStart { get; init; }

        public Action? OnStop { get; init; }

        public int StartCalls => Volatile.Read(ref _starts);

        public int StopCalls => Volatile.Read(ref _stops);

        public int DisposeCalls => Volatile.Read(ref _disposes);

        public bool StopSawStartReturned => Volatile.Read(ref _stopSawStartReturned);

        public void StartWatching(ISecretsManagerConfigurationProvider provider)
        {
            Interlocked.Increment(ref _starts);
            OnStart?.Invoke(provider);
            Volatile.Write(ref _startReturned, true);
        }

        public void StopWatching()
        {
            Interlocked.Increment(ref _stops);
            Volatile.Write(ref _stopSawStartReturned, Volatile.Read(ref _startReturned));
            OnStop?.Invoke();
        }

        public void Dispose() => Interlocked.Increment(ref _disposes);
    }

    private sealed class CallbackProcessor : ISecretProcessor
    {
        private readonly Action _callback;

        public CallbackProcessor(Action callback)
        {
            _callback = callback;
        }

        public Dictionary<string, string?> GetConfigurationData(SecretsManagerConfigurationSource source, string secretString)
        {
            _callback();
            return SecretsProcessor.Json.GetConfigurationData(source, secretString);
        }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public ConcurrentQueue<string> EventQueue { get; } = new();

        public IEnumerable<string> Events => EventQueue.ToArray();

        public bool ThrowOnCreateLogger { get; set; }

        public bool ThrowOnLog { get; set; }

        public Action<EventId>? OnLog { get; init; }

        public ILogger CreateLogger(string categoryName) =>
            ThrowOnCreateLogger
                ? throw new InvalidOperationException("logger factory is disposed")
                : new RecordingLogger(this);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly RecordingLoggerFactory _factory;

            public RecordingLogger(RecordingLoggerFactory factory)
            {
                _factory = factory;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _factory.EventQueue.Enqueue(eventId.Name ?? string.Empty);
                _factory.OnLog?.Invoke(eventId);

                if (_factory.ThrowOnLog)
                {
                    throw new InvalidOperationException("logger is disposed");
                }
            }
        }
    }

    private sealed class MetricsCollector : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly ConcurrentQueue<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> _counters = new();

        public MetricsCollector()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == MeterDescriptors.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                _counters.Enqueue((instrument.Name, value, tags.ToArray())));

            _listener.Start();
        }

        public int CountMeasurements(string instrumentName, string secretName) =>
            _counters.Count(m =>
                m.Instrument == instrumentName &&
                m.Tags.Any(t => t.Key == "aws.secretsmanager.secret.id" && t.Value as string == secretName));

        public void Dispose() => _listener.Dispose();
    }
}