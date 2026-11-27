# 19. Provider disposal stops its watcher

- Status: Accepted
- Date: 2026-11

## Context

[ADR-0007](0007-refresh-via-pluggable-watcher.md) made refresh an opt-in
`IConfigurationWatcher`, started by the provider after a successful initial
load. It recorded as a consequence that "the configuration system does not
dispose sources, so long-lived hosts keep the timer for the process lifetime".

That holds for sources, but not for providers: `ConfigurationRoot.Dispose()`
and `ConfigurationManager.Dispose()` dispose every provider that implements
`IDisposable`, and `ConfigurationManager` also disposes the old providers when
a change to `Sources` (anything other than `Add`) rebuilds them. A host
disposes the configuration it owns together with its service provider.
`SecretsManagerConfigurationProvider` does not implement `IDisposable`, so
none of that reaches the watcher:

- A polling timer outlives the configuration it belongs to. In a host whose
  configuration lives as long as the process this is invisible, but processes
  that create many configuration roots — integration tests building a host per
  test, or a disposed and recreated host — leak one running timer per root.
- The leaked timer keeps the provider, its AWS client, its logger factory and
  the loaded secret values reachable after the configuration is gone.
- When it fires, it calls an AWS client and a logger factory that the host may
  already have disposed. The watcher does not swallow exceptions (ADR-0007),
  so without an `OnReloadException` handler the failure is thrown on a timer
  thread and ends the process.

A consumer cannot fix this from outside: the provider is sealed, the watcher is
started inside `Load()`, and `WithPollingWatcher(...)` creates the watcher
instance on the consumer's behalf, so the consumer has no natural owner for it.

## Decision

**The provider is disposable.** `SecretsManagerConfigurationProvider`
implements `IDisposable`, so disposing the configuration root stops refresh.
It does not implement `IAsyncDisposable`: configuration roots dispose
providers synchronously.

**The provider captures its watcher at construction.** The watcher, and
whether the library owns it, are read from the source once, when the provider
is created. Changing `Source.ConfigurationWatcher` afterwards has no effect, in
line with the key transformer snapshot of
[ADR-0012](0012-snapshot-key-transformers-at-build-time.md). This guarantees
that disposal stops the watcher that was actually started.

**Ownership follows who created the watcher.**

| Watcher set via | Owner | On provider disposal |
| --- | --- | --- |
| `WithPollingWatcher(...)` (any overload) | library | stopped, then disposed |
| `WithConfigurationWatcher(...)` | caller | stopped, not disposed |
| `ConfigurationWatcher` property | caller | stopped, not disposed |

Assigning the `ConfigurationWatcher` property always makes the watcher
caller-owned, including after an earlier `WithPollingWatcher(...)`.
`IConfigurationWatcher` is not changed — adding `IDisposable` to it would break
every custom implementation — so the provider disposes an owned watcher only if
it happens to implement `IDisposable`.

**The watcher is started at most once per provider.** It is started after the
first successful load; later loads (`IConfigurationRoot.Reload()` calls
`Load()` again) do not start it a second time. A watcher whose `StartWatching`
throws is treated as not started — it is not stopped, since the failure may
mean it belongs to another provider — and the next successful load tries
again. "Stopped" on disposal means `StopWatching()` is called only if the
provider started the watcher; a library-owned watcher is disposed either way.

**Publication is atomic with disposal; everything else checks for it.**

- New data is published under the same lock that marks the provider
  disposed, so either the data is published before disposal, or it is not
  published at all. `Data`, `CurrentVersionId` and `LastLoadedAt` are never
  changed by `Load()` or `Reload()` after `Dispose()` returns. (The inherited
  public `Set()` remains the caller's business.)
- Failure handling checks for disposal (`when (IsDisposed)`), so a late
  failure is ignored whatever caused it: the cancelled fetch, an AWS client
  or logger factory the host has already disposed, or an unrelated error. The
  activity, if any, records a `disposed` event instead.
- A step that already started before disposal finishes, so reporting can
  overlap `Dispose()`: a failure observed just before disposal is still
  logged and passed to its callback, and the change notifications of data
  published just before disposal still run.
- `Reload()` reached by disposal returns silently. `Load()` reached by
  disposal *before* publishing throws `ObjectDisposedException` (with no inner
  exception, see [ADR-0017](0017-binary-secret-decode-failures-omit-the-payload.md));
  after publishing it returns normally — the data was loaded — and a late
  exception (from a change callback, the logger or the watcher start) is
  ignored.

**Disposal is final and does not join loads or reloads.**

- `Dispose()` is idempotent. It marks the provider disposed, cancels an
  in-flight secret fetch, then stops and (if owned) disposes the watcher.
  Cancelling first matters: a custom watcher's `StopWatching()` may wait for
  its own worker, and that worker may be the reload blocked on the fetch.
  Each step runs even if the previous one throws; the exception propagates.
- `Dispose()` never calls `StopWatching()` or `_disposeCts.Cancel()` while
  holding a lock and never waits for a load or reload fetch, so it may be
  called from any thread — including from a change-token callback raised by
  the reload. Watcher start and disposal serialize on the provider lock so a
  watcher cannot start after disposal.
- `Dispose()` can still block in code it does not own: cancellation callbacks
  the AWS client registered on the fetch token, and `StopWatching()` of a
  caller-owned watcher, run synchronously.
- `Reload()` after disposal returns immediately: no AWS call, no callback, no
  log, no metric. Late timer callbacks and custom watchers that call
  `Reload()` after the configuration is gone are both harmless.
- `Load()` after disposal throws `ObjectDisposedException`. Unlike `Reload()`
  it is called explicitly and synchronously (`IConfigurationRoot.Reload()`),
  so the caller should learn that the configuration is no longer usable.

**The polling watcher becomes safe to dispose at any point.**
`SecretsManagerPollingWatcher` serializes start, stop and disposal, so a timer
can never be created after, or survive, disposal. After `Dispose()` it behaves
as stopped, and `StartWatching` throws `ObjectDisposedException` rather than
the misleading "already started" error. A second `StartWatching`, sequential or
concurrent, still throws "already started". A reload callback that was already
running when the watcher stopped finishes; if the watcher was restarted in the
meantime, that callback may shift the new timer's next due time — restart
isolation is not a goal.

## Consequences

- Disposing a configuration root, or a host, stops polling and removes the
  timer's retention path. (The disposed provider still references its source,
  client and last data until it is collected.) Tests can assert that disposing
  the configuration stops polling.
- Behavioural change, not caught by package validation (adding an interface to
  a sealed class is compatible): code that disposed a configuration root and
  relied on the provider continuing to refresh will no longer get refreshes.
  Release notes must state it.
- Caller-supplied watchers now receive `StopWatching()` when the configuration
  is disposed. The provider started them, so it stops them; their own
  resources remain the caller's to release.
- Work that started just before disposal may still be completing after
  `Dispose()` returns: change notifications for data published just before
  disposal, or the log entry and `OnReloadException` call for a failure
  observed just before it. A fetch that is still running is only cancelled,
  never awaited.
- Late outcomes are ignored regardless of their cause, so a genuine secret
  failure that happens to complete after disposal goes unreported. The
  configuration is gone, so there is no one left to act on it.
- `Load()` and `Reload()` deliberately differ after disposal: the background
  path is a silent no-op, the explicit path throws.
- `IConfigurationRoot.Reload()` with a watcher no longer fails with "already
  started".
- Providers constructed directly instead of via `Build()` capture the watcher
  set on the source at construction time, the same as built ones.

Out of scope, recorded as follow-ups:

- **Rebuilding a source.** The guard in `Build()` (C4) still rejects a second
  build, so a `ConfigurationManager` rebuild throws rather than leaking. With
  disposal in place, giving each provider its own library-owned watcher would
  allow lifting that guard for library-owned watchers.
- **The library-created AWS client.** `Build()` creates an
  `AmazonSecretsManagerClient` when none is supplied and never disposes it; it
  is the same ownership gap for a cheaper resource.

## Update (2026-11-27)

The lock-held startup description above is refined: startup reserves its state
under the provider lock, then calls `StartWatching()` outside it. If disposal
overlaps startup, it cancels the fetch and leaves watcher cleanup to the
starting thread after start returns or fails. No provider lock is held during
watcher, cancellation, or change callbacks. A successful start is stopped
once; a failed-start owned watcher is disposed but not stopped.

Source assignment replaces the watcher and ownership; polling timers are made
only by `StartWatching()`, so an overwritten, unstarted poller needs no
disposal. The provider captures watcher and ownership in its constructor.
Ordinary Activity listener semantics are unchanged: only failures caught by a
load/reload operation are suppressed after disposal; pre-committed change
notifications and diagnostics may finish after disposal.

## Related

- [ADR-0006](0006-error-handling-and-optional-secrets.md) — outcomes reached
  after disposal are no longer enveloped, logged or passed to the exception
  callbacks.
- [ADR-0007](0007-refresh-via-pluggable-watcher.md) — the watcher abstraction
  whose lifecycle this decision completes.
- [ADR-0012](0012-snapshot-key-transformers-at-build-time.md) — the
  capture-at-build precedent followed for the watcher.
