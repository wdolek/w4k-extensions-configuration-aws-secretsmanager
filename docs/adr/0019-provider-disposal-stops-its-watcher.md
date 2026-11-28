# 19. Provider disposal stops its watcher

- Status: Accepted
- Date: 2026-11

## Context

[ADR-0007](0007-refresh-via-pluggable-watcher.md) made refresh an opt-in
`IConfigurationWatcher`, started by the provider after a successful initial
load, and noted that "the configuration system does not dispose sources, so
long-lived hosts keep the timer for the process lifetime".

That holds for sources, not for providers: `ConfigurationRoot.Dispose()` and
`ConfigurationManager.Dispose()` dispose every provider implementing
`IDisposable`, and a host disposes the configuration it owns together with its
service provider. Because `SecretsManagerConfigurationProvider` was not
`IDisposable`, disposing a configuration root left the polling timer running:

- Short-lived hosts or integration tests building a host per test leaked one
  running timer per configuration root, keeping the provider, AWS client,
  logger factory and loaded secrets reachable.
- Subsequent timer ticks called an AWS client or logger factory the host had
  already disposed; without `OnReloadException`, the exception escaped on a
  timer thread and terminated the process.

Separately, `WithPollingWatcher(...)` instantiated `SecretsManagerPollingWatcher`
eagerly on the non-disposable `SecretsManagerConfigurationSource`, leaving no
clean owner for the `IDisposable` watcher instance.

## Decision

- **`SecretsManagerConfigurationProvider` implements `IDisposable`.** Disposing
  the provider cancels any in-flight secret fetch and releases its watcher.
- **Polling watchers are created via an internal factory, not on the source.**
  `WithPollingWatcher(...)` validates its arguments and records a factory on
  `SecretsManagerConfigurationSource`. The watcher instance is created when the
  provider is constructed, so an owned `IDisposable` watcher never lives
  outside an `IDisposable` provider.
- **Ownership follows who instantiated the watcher, encapsulated in
  `WatcherLifetime`:**

  | Configured via | Instantiated by | On provider disposal |
  | --- | --- | --- |
  | `WithPollingWatcher(...)` | provider (factory) | stopped (if started) and disposed |
  | `WithConfigurationWatcher(...)` / `ConfigurationWatcher` | caller | stopped (if started), not disposed |

  Setting `ConfigurationWatcher` clears any polling factory and vice versa; the
  last configuration before provider construction wins, and mutating the source
  afterwards does not affect existing providers
  ([ADR-0012](0012-snapshot-key-transformers-at-build-time.md)).
- **`WatcherLifetime` starts the watcher at most once.** Subsequent `Load()`
  calls (such as `IConfigurationRoot.Reload()`) do not start it a second time;
  a failed `StartWatching` leaves it idle so a later load can retry. Lock
  scopes cover state transitions only, never external watcher calls.
- **Post-disposal behaviour matches call intent.** After disposal, `Reload()`
  is a silent no-op and in-flight reload failures are ignored; `Load()` throws
  `ObjectDisposedException`.
- **`SecretsManagerPollingWatcher` serializes lifecycle operations.** `StartWatching`,
  `StopWatching`, `Dispose`, `DisposeAsync` and timer rescheduling synchronize
  on an internal lock; `StartWatching` after disposal throws
  `ObjectDisposedException`.

## Consequences

- Disposing a configuration root or host stops polling immediately and disposes
  library-created timers.
- Ownership requires no boolean flags or property-setter disposal on the
  source: caller-supplied instances live in `ConfigurationWatcher`, while
  library-owned instances are created per provider from the factory.
- Behavioural changes for release notes: refresh stops when the configuration
  root is disposed, and caller-supplied watchers now receive `StopWatching()`
  on provider disposal.
- `IConfigurationRoot.Reload()` with a configured watcher no longer fails with
  "already started".
