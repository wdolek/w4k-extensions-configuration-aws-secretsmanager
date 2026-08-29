# 7. Refresh via pluggable watcher

- Status: Accepted
- Date: 2024-02

## Context

Secrets rotate. Applications that cannot be restarted on every rotation need to
pick up the new value at runtime, which the configuration system already
supports through change tokens (`IOptionsSnapshot`, `IOptionsMonitor`).

AWS does not offer a push notification that a configuration provider could
subscribe to cheaply. The realistic options are polling `GetSecretValue`, or
reacting to an external signal (EventBridge rotation event, SQS message,
an admin endpoint, a Kubernetes signal). Each `GetSecretValue` call is billed,
so a built-in default poll would silently add cost to every consumer — directly
against the "nothing runs unless asked for" principle
([ADR-0002](0002-minimal-dependency-footprint.md)).

## Decision

Model refresh as an abstraction, with polling as the only built-in
implementation and **no refresh by default**:

```csharp
public interface IConfigurationWatcher
{
    void StartWatching(ISecretsManagerConfigurationProvider provider);
    void StopWatching();
}
```

- `SecretsManagerConfigurationSource.ConfigurationWatcher` is `null` by default —
  the provider loads once and never calls AWS again.
- `SecretsManagerPollingWatcher` is opt-in via
  `WithPollingWatcher(TimeSpan interval)`. It uses `TimeProvider.CreateTimer`
  rather than `System.Threading.Timer` directly, which makes it testable with
  `FakeTimeProvider` and lets consumers inject their own clock.
- The watcher is handed `ISecretsManagerConfigurationProvider`, not the concrete
  provider, so custom watchers (event-driven, endpoint-triggered) can call
  `Reload()` without depending on internals.
- **The watcher starts only after a successful initial load** — including for
  optional secrets. Starting a poller for a provider that never loaded would
  hide misconfiguration behind background retries.
- **The watcher does not swallow exceptions** from `Reload()` (asserted by a
  unit test). Silent background failures are worse than loud ones; suppression
  is an explicit choice via `OnReloadException`
  ([ADR-0006](0006-error-handling-and-optional-secrets.md)).
- `Reload()` guards against overlapping executions with
  `Interlocked.Exchange(ref _reloadInProgress, 1)`; a slow fetch cannot pile up
  concurrent calls.
- A watcher instance owns one timer and throws if `StartWatching` is called
  twice — instances must not be shared between sources.

## Consequences

- Zero background activity and zero extra AWS cost unless refresh is requested.
  The README states the cost implication explicitly.
- Polling interval is a direct cost/staleness trade-off owned by the consumer.
- Event-driven refresh is possible without library changes.
- The watcher's timer callback runs on a thread-pool thread and performs the same
  blocking fetch as `Load()` ([ADR-0001](0001-configuration-source-with-synchronous-load.md)).
  Acceptable given the low frequency.
- An unhandled reload exception escapes on a timer thread; consumers relying on
  refresh must configure `OnReloadException`.
- `SecretsManagerPollingWatcher` implements `IDisposable`/`IAsyncDisposable`, but
  the configuration system does not dispose sources, so long-lived hosts keep the
  timer for the process lifetime — which is the intended behaviour.
