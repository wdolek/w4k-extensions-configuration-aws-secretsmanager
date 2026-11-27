# Correctness

Bugs and latent defects. Do these first; all are non-breaking and shippable in
a 2.x patch/minor unless noted.

---

## C1. Binary secrets are base64-decoded twice

**Status:** done · **Breaking:** no (fixes a broken path) · **Effort:** S

### Why

`SecretFetcher` reads `response.SecretBinary` as text and then calls
`Convert.FromBase64String` on it. The AWS SDK has *already* decoded the base64
from the wire — `GetSecretValueResponseUnmarshaller` unmarshals `SecretBinary`
with `MemoryStreamUnmarshaller`, whose implementation is:

```csharp
byte[] bytes = Convert.FromBase64String(context.ReadText(ref reader));
MemoryStream stream = new MemoryStream(bytes, 0, bytes.Length, true, true);
```

So `response.SecretBinary` already holds the raw plaintext bytes. Decoding again
throws `FormatException` (wrapped into `SecretRetrievalException`) for any real
binary secret whose content is not itself base64.

Not observed in practice because secrets created with `--secret-string` / the
console always populate `SecretString`, and `GetSecret` returns from that branch
first. Only secrets written with `SecretBinary` reach the broken code.

### Where

`src/W4k.Extensions.Configuration.Aws.SecretsManager/SecretFetcher.cs:25-32`

### Change

```csharp
// before
if (response.SecretBinary is not null)
{
    using var reader = new StreamReader(response.SecretBinary, leaveOpen: false);
    var encodedString = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    var secretString = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedString));

    return new(response.VersionId, secretString);
}

// after
if (response.SecretBinary is not null)
{
    using var binary = response.SecretBinary;
    var secretString = Encoding.UTF8.GetString(binary.GetBuffer(), 0, (int)binary.Length);

    return new(response.VersionId, secretString);
}
```

Notes:

- `MemoryStreamUnmarshaller` constructs the stream with `publiclyVisible: true`,
  so `GetBuffer()` is safe. If you would rather not rely on that, use
  `binary.ToArray()` — one extra copy, still correct.
- Add `using System.Text;` at the top and drop the fully-qualified
  `System.Text.Encoding` usage.
- The method no longer awaits anything in this branch; it still awaits
  `GetSecretValueAsync`, so keep it `async`.

### Tests

1. Fix `tests/.../SecretFetcherShould.cs`, test `ReturnBinarySecret`
   (lines ~44-46). It currently pre-encodes the payload as base64 *into* the
   stream, which encodes the bug. Change to:

   ```csharp
   var secretContent = """{ "le_secret": "MZ/X" }""";
   using var secretBinary = new MemoryStream(Encoding.UTF8.GetBytes(secretContent));
   ```

   The assertion `result.Value == secretContent` then verifies correct
   behaviour.

2. Add an integration test that round-trips a real binary secret. Extend
   `tests/.../IntegrationTests/AmazonSecretsManagerExtensions.cs`:

   ```csharp
   public static Task CreateBinarySecret(this IAmazonSecretsManager client, string secretName, byte[] secretValue) =>
       client.CreateSecretAsync(
           new CreateSecretRequest
           {
               Name = secretName,
               SecretBinary = new MemoryStream(secretValue),
               Description = "W4k.Extensions.Configuration.Aws.SecretsManager integration tests secret",
           });
   ```

   Add a `BinarySecretName` + payload to `TestSecrets.cs`, create it in
   `SecretsManagerTestFixture`, and assert in `FetchTests` that the JSON binds
   the same as the equivalent string secret. This is the only way to catch a
   regression here, since the SDK's decode step is not exercised by mocks.

---

## C2. `CancellationTokenSource` is never disposed

**Status:** done · **Breaking:** no · **Effort:** XS

### Why

Each `Load()` / `Reload()` allocates a `CancellationTokenSource` with a timer
and abandons it. The underlying timer registration stays rooted until the CTS is
finalized/collected. With a polling watcher this leaks one per poll, forever.

### Where

- `src/.../SecretsManagerConfigurationProvider.cs:52` (`Load`)
- `src/.../SecretsManagerConfigurationProvider.cs:105` (`Reload`)

### Change

Both places:

```csharp
// before
var cts = new CancellationTokenSource(Source.Timeout);

// after
using var cts = new CancellationTokenSource(Source.Timeout);
```

The `using` must be inside the `try` block (it already is) so the existing
`catch` still observes `OperationCanceledException` from a timeout.

### Tests

No behavioural change to assert. Existing tests must stay green.

---

## C3. `_currentSecretVersionId` is read and written across threads without a barrier

**Status:** done · **Breaking:** no · **Effort:** XS

### Why

`_currentSecretVersionId` is a plain `string?` field. It is written on the thread
running `Load()` (usually the startup thread) and read/written on the
`TimeProvider` timer thread in `Reload()`. There is no volatile access or lock,
so the change-detection comparison at
`SecretsManagerConfigurationProvider.cs:111` may observe a stale value. Benign on
x86/x64 in practice, not guaranteed by the memory model.

### Where

`src/.../SecretsManagerConfigurationProvider.cs:17, 111, 121, 176`

### Change

```csharp
// read (line ~111)
var currentVersionId = Volatile.Read(ref _currentSecretVersionId);
if (string.Equals(secret.VersionId, currentVersionId, StringComparison.Ordinal))

// and reuse `currentVersionId` for `previousVersionId` at line ~121

// write, in SetData (line ~176)
Volatile.Write(ref _currentSecretVersionId, versionId);
```

`Data` is a reference assignment and is fine as-is, but for consistency the
whole `SetData` body is the mutation point — keep both writes there.

### Tests

None required. Do not attempt to write a race-condition test.

---

## C4. `Build()` is not idempotent and shares the watcher instance

**Status:** done · **Breaking:** no · **Effort:** S

### Why

`SecretsManagerConfigurationSource.Build()` mutates the source and returns a new
provider each call. If the same source instance is built twice (source reused
across two builders, or a builder whose sources are built more than once), two
providers end up sharing one `IConfigurationWatcher`. The second
`StartWatching` throws `InvalidOperationException("Watcher is already started,
have you re-used watcher instance?")` from inside `Load()`, which surfaces to
the user as a confusing `SecretRetrievalException` on a secret that fetched
fine.

### Where

- `src/.../SecretsManagerConfigurationSource.cs:97-111` (`Build`)
- `src/.../SecretsManagerPollingWatcher.cs:54-58` (`StartWatching`)

### Change

Pick one:

- **Option A (minimal).** Track in the source that it has been built, and throw
  a clear `InvalidOperationException` from `Build()` on the second call:
  *"Configuration source has already been built; create a new source per
  builder."* Fails fast with an actionable message instead of failing later
  inside `Load()`.
- **Option B (more work, better).** Make the watcher a factory on the source —
  `Func<IConfigurationWatcher>? ConfigurationWatcherFactory` — so each provider
  gets its own instance. This changes public API (`ConfigurationWatcher`
  property), so it is **v3** material. If chosen, keep
  `WithConfigurationWatcher(IConfigurationWatcher)` working by wrapping the
  instance in a factory that returns it once and throws afterwards.

Recommend Option A now, revisit Option B if/when v3 happens.

### Tests

`tests/.../SecretsManagerConfigurationProviderShould.cs` — add a test that
calling `Build()` twice on the same source throws `InvalidOperationException`
with a message mentioning reuse.

---

## C5. `ActivitySource` version string has drifted from the package version

**Status:** done · **Breaking:** no · **Effort:** XS

### Why

`ActivityDescriptors.Source` is constructed with version `"2.1"` while
`VersionPrefix` in the csproj is `2.3.0`. ADR-0010 states the two must track
each other. A hardcoded literal guarantees this drifts again.

### Where

- `src/.../Diagnostics/ActivityDescriptors.cs`
- `src/.../W4k.Extensions.Configuration.Aws.SecretsManager.csproj:14`

### Change

Derive the version from the assembly instead of hardcoding:

```csharp
internal static ActivitySource Source { get; } = new(
    ActivitySourceName,
    typeof(ActivityDescriptors).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion);
```

Note `InformationalVersion` includes the SourceLink commit suffix
(`2.3.0+abc1234`) on CI builds. If that is undesirable in trace metadata, trim
at the first `+`:

```csharp
var informational = /* as above */ ?? "";
var version = informational.Split('+')[0];
```

Keep it a `static` property initialised once; do not compute per activity.

### Tests

`tests/` — optional assertion that `ActivityDescriptors` exposes a non-empty
version. Low value; the real fix is removing the literal.

---

## C6. Sync-over-async runs on the caller's thread

**Status:** done · **Breaking:** no · **Effort:** S · **Priority:** low

### Why

`Load()` and `Reload()` block with `.ConfigureAwait(false).GetAwaiter().GetResult()`
on whatever thread called them. `ConfigureAwait(false)` protects the SDK's own
continuations, so this is safe with the current AWS SDK. It is not robust if a
caller has a real `SynchronizationContext`, and `WithTimeout` only works because
the SDK honours the cancellation token — if that ever stopped being true, the
call would block indefinitely despite the configured timeout.

ADR-0001 deliberately keeps `Load()` synchronous; this task does **not** change
that. It only moves the awaited work onto the thread pool.

### Where

- `src/.../SecretsManagerConfigurationProvider.cs:53-56`
- `src/.../SecretsManagerConfigurationProvider.cs:106-109`

### Change

```csharp
// before
var secret = _secretFetcher.GetSecret(secretName, secretVersion, cts.Token)
    .ConfigureAwait(false)
    .GetAwaiter()
    .GetResult();

// after
var secret = Task
    .Run(() => _secretFetcher.GetSecret(secretName, secretVersion, cts.Token), cts.Token)
    .GetAwaiter()
    .GetResult();
```

Trade-off to weigh before doing this: it costs one thread-pool hop per load and
slightly obscures the stack trace on failure. It is defensive rather than
fixing an observed bug — reject it if the added indirection is not worth it.

### Tests

Existing provider tests must stay green, including the timeout test. Verify the
exception thrown on timeout is still unwrapped correctly by `HandleException`
(`Task.Run` will surface `OperationCanceledException`, not
`AggregateException`, via `GetAwaiter().GetResult()`).

---

## C7. Non-UTF-8 binary secrets are silently corrupted

**Status:** done · **Breaking:** behavioural (silent corruption → loud failure) · **Effort:** XS

### Why

Post-C1, `SecretFetcher` holds the correct raw bytes but decodes them with
`Encoding.UTF8`, which **replaces** invalid byte sequences with U+FFFD instead
of throwing. A binary secret holding a certificate, signing key or p12
therefore round-trips into silently mangled configuration values — the worst
failure mode, because nothing ever complains.

`SecretsProcessor.Json` happens to throw later for such payloads (mangled
bytes are not JSON), but with F1 (plain-text processor) the corrupted value
would flow straight into configuration. The plain-text processor makes this
defect user-visible, so fix it alongside or before F1.

### Where

`src/.../SecretFetcher.cs:30`

### Change

Decode strictly, so a non-UTF-8 payload fails loudly at fetch time instead of
corrupting silently:

```csharp
private static readonly UTF8Encoding Utf8Strict =
    new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

// in GetSecret, binary branch:
var secretString = Utf8Strict.GetString(binary.GetBuffer(), 0, (int)binary.Length);
```

- The thrown `DecoderFallbackException` is enveloped into
  `SecretRetrievalException` by the existing provider error handling, so no
  new plumbing is needed.

  > **Amendment ([ADR-0017](../adr/0017-binary-secret-decode-failures-omit-the-payload.md)):**
  > that envelope leaked the secret - `DecoderFallbackException.Message` embeds
  > the offending raw bytes, and both the logger call and `Exception.ToString()`
  > on the outer exception would print them. `SecretFetcher` now catches the
  > exception itself and raises a sanitized `SecretRetrievalException` with no
  > inner exception; this *is* new plumbing, added post-release per ADR-0017.

- Alternative rejected: base64-encoding the payload in the fetcher so a
  processor could decode it — that changes what *every* processor sees and
  breaks the JSON-on-binary-secret path that C1's tests pin down.
- The real fix — handing processors raw bytes — is a v3 contract change that
  should ride along with **A4**; see F8 for how `Base64SecretProcessor` would
  consume it. C7 is the stopgap that makes the 2.x failure mode loud.

### Risk

Behavioural, not API-breaking: secrets that previously "worked" (produced
U+FFFD-mangled values) now throw at load. Only affects secrets written via
`SecretBinary` with non-UTF-8 content — a population that was broken anyway.
Call it out in release notes.

### Tests

- `tests/.../SecretFetcherShould.cs` — binary payload with invalid UTF-8
  (e.g. `[0xC3, 0x28]`) throws; valid UTF-8 payload still decodes.
- Integration test optional (a `SecretBinary` secret with non-UTF-8 bytes),
  follows the C1 pattern.

---

## C8. Disposing the configuration does not stop the watcher

**Status:** done · **Breaking:** behavioural (refresh stops on disposal) · **Effort:** M
**Decision:** [ADR-0019](../adr/0019-provider-disposal-stops-its-watcher.md)

### Why

`SecretsManagerConfigurationProvider` is not `IDisposable`, so disposing a
`ConfigurationRoot` / `ConfigurationManager` (or the host owning it) never
reaches the watcher. The polling timer keeps running, keeps the provider, AWS
client, logger factory and secret values reachable, and eventually calls a
client the host has disposed — on a timer thread, where an unhandled reload
failure ends the process. Reported by a consumer as prodigy work item #143.

Two latent defects in the same code are fixed along the way:

- `IConfigurationRoot.Reload()` calls `Load()` again, which calls
  `StartWatching` again; the polling watcher throws "already started", which
  surfaces as a `SecretRetrievalException` for a secret that loaded fine.
- `Reload()` sets its overlap guard and then calls
  `Source.LoggerFactory.CreateLogger(...)` **outside** the `try/finally` that
  resets the guard. If logger creation throws (e.g. a disposed logger
  factory), the guard is never reset and every later reload is skipped
  silently.

### Where

- `src/.../SecretsManagerConfigurationProvider.cs`
- `src/.../SecretsManagerConfigurationSource.cs` (`ConfigurationWatcher`
  property, `WithPollingWatcher(...)` overloads)
- `src/.../SecretsManagerPollingWatcher.cs`

### Change

#### 1. Source: record ownership

```csharp
private IConfigurationWatcher? _configurationWatcher;

public IConfigurationWatcher? ConfigurationWatcher
{
    get => _configurationWatcher;
    set => SetConfigurationWatcher(value, isOwned: false); // anything assigned from outside is caller-owned
}

internal bool OwnsConfigurationWatcher { get; private set; }

internal void SetConfigurationWatcher(IConfigurationWatcher? watcher, bool isOwned)
{
    _configurationWatcher = watcher;
    OwnsConfigurationWatcher = isOwned;
}
```

`SetConfigurationWatcher` is the only place the watcher and its ownership
are written. All four `WithPollingWatcher(...)` overloads call it with
`isOwned: true`; `WithConfigurationWatcher(...)` and the property setter
with `isOwned: false`.

#### 2. Provider: state

```csharp
public sealed class SecretsManagerConfigurationProvider
    : ConfigurationProvider, ISecretsManagerConfigurationProvider, IDisposable
{
    private const int WatcherIdle = 0, WatcherStarting = 1, WatcherStarted = 2, WatcherReleased = 3;

    private readonly IConfigurationWatcher? _watcher;   // captured in ctor
    private readonly bool _ownsWatcher;                 // captured in ctor
    private readonly CancellationTokenSource _disposeCts = new(); // see "CTS" below
    private readonly Lock _lock = new();                // protects _disposed, _watcherState and data publication
    private int _watcherState;
    private bool _disposed;

    private bool IsDisposed => Volatile.Read(ref _disposed);
}
```

No watcher method, no callback and no logging ever runs while `_lock` is held.
The lock only protects `_disposed`, `_watcherState` transitions and the data
assignment.

#### 3. Provider: publication boundary

```csharp
// publishes data unless disposed; never calls OnReload() itself
private bool TryPublish(string versionId, Dictionary<string, string?> data)
{
    lock (_lock)
    {
        if (_disposed) return false;
        Volatile.Write(ref _currentSecretVersionId, versionId);
        Volatile.Write(ref _lastLoadedUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        Data = data;
        return true;
    }
}
```

`SetData` is replaced by `TryPublish` followed by `OnReload()` outside the
lock. Everything else (diagnostics, failure reporting) is gated by a plain
`IsDisposed` check just before it runs — no lock, no "outcome" bookkeeping.
A step that passed its check may overlap disposal; that is accepted
(ADR-0019).

#### 4. Provider: `Load()` and `Reload()`

- Both keep standard `using var activity = ...`, create `logger` inside `try`,
  fetch via a shared `FetchSecret(...)` helper using a linked CTS with
  `using var cts` + `cts.CancelAfter(Source.Timeout)`, and ignore late failures
  via `catch (Exception) when (IsDisposed)`.
- `Load()` throws `ObjectDisposedException` at entry if already disposed, and
  after `try/catch` if `!published && IsDisposed` (with no inner exception).
- `Reload()` returns immediately if `IsDisposed` or already in progress, and
  resets `_reloadInProgress` in an outer `try/finally` covering the whole body
  (including `using var activity` and logger creation).

#### 5. Provider: watcher start / release and `Dispose()`

`StartWatcherOnce()` and `Dispose()` coordinate `_disposed` and
`_watcherState` under `_lock`, releasing the lock before invoking
`_watcher.StartWatching`, `_watcher.StopWatching`, or `IDisposable.Dispose`.
If `Dispose()` runs while `_watcherState == WatcherStarting`, it leaves
cleanup to `StartWatcherOnce()`'s `finally`, which stops (only if
`StartWatching` succeeded) and disposes an owned watcher as soon as
`StartWatching` finishes. `Dispose()` always cancels `_disposeCts` before
releasing the watcher.

#### 6. Polling watcher

Add `private readonly Lock _lock = new()` and `private bool _disposed`;
`_timer` is only read or written under `_lock`.

- `StartWatching`: under the lock — `ObjectDisposedException.ThrowIf`, the
  existing "already started" guard, then create and assign the timer (initial
  due time is always > 0, so the callback cannot run inside the lock).
- `StopWatching`: under the lock, take the timer and set `_timer = null`;
  outside the lock, `Dispose()` it (no preceding `Change` — one fewer
  failure point between detaching and disposing).
- `Dispose()`: under the lock, set `_disposed` and take the timer; outside,
  dispose it. `DisposeAsync()`: same, `await` the timer's `DisposeAsync()`.
  Both idempotent.
- `Reschedule`: under the lock, read `_timer`; outside, `Change(...)` it,
  keeping the `ObjectDisposedException` catch. If a stop detaches that timer
  in between, the stop disposes it right after, so a re-arm cannot outlive
  the stop. A callback that was already running finishes after `StopWatching`
  returns, and one from before a restart may shift the new timer's next due
  time — accepted, restart isolation is not a goal (ADR-0019).

### Tests

Use hand-written fakes (an `AmazonSecretsManagerClient` subclass overriding
`GetSecretValueAsync`, which is virtual; a recording `IConfigurationWatcher`
that also implements `IDisposable`), `TaskCompletionSource` barriers and
`FakeTimeProvider` — no sleeps. `FakeTimeProvider.Advance()` runs callbacks
synchronously on the calling thread: when a test needs a fetch to block, call
`provider.Reload()` (or `Advance`) on a worker task, and await with a bounded
`WaitAsync` timeout used only as a hang guard.

Provider (new `SecretsManagerConfigurationProviderDisposalShould.cs`):

- Disposing a root built with `ConfigurationBuilder` + `WithPollingWatcher`
  stops polling: the call count does not grow after advancing several
  intervals. Same through `ConfigurationManager.Dispose()`.
- Ownership: each of the four `WithPollingWatcher` overloads yields a watcher
  that is disposed (its `StartWatching` then throws `ObjectDisposedException`);
  `WithConfigurationWatcher`, the property set directly, and the property set
  after `WithPollingWatcher` yield a watcher that gets exactly one
  `StopWatching()` and no `Dispose()`; the same via the
  `Action<SecretsManagerConfigurationSource>` overload.
- Watcher never started (initial load failed and was ignored) → `Dispose()`
  does not call `StopWatching()`, still disposes an owned watcher.
- Initial load ignored, later `Load()` succeeds → watcher started exactly once.
- `IConfigurationRoot.Reload()` with a watcher → no exception, watcher started
  once.
- `StartWatching` throws → state reverts; next successful `Load()` retries.
- `Dispose()` twice → stopped/disposed once. Caller watcher's
  `StopWatching()` throws → exception propagates from `Dispose()`, the fetch
  token was already cancelled, a second `Dispose()` does not call it again.
- `Reload()` after `Dispose()` → no AWS call, no reload token, no callback.
  `Load()` after `Dispose()` → `ObjectDisposedException`, no AWS call.
- Re-entrancy: a recording watcher whose `StartWatching` calls
  `provider.Dispose()` synchronously → `Load()` returns normally (data was
  published before disposal), `StopWatching()` is called exactly once after
  `StartWatching` returns, no deadlock.
- Startup held behind a barrier while `Dispose()` runs on another thread →
  `StopWatching()` exactly once, after `StartWatching` is released. Same with
  a `StartWatching` that throws after the barrier → no `StopWatching()`, owned
  watcher disposed; a later `Load()` throws `ObjectDisposedException`.
- Custom watcher whose `StopWatching()` waits for its worker, and the worker
  is inside a reload blocked on a cancellation-honouring fetch → `Dispose()`
  completes (cancel-before-stop), guarded by a `WaitAsync` hang timeout.
- `Dispose()` from a reload-token callback during `Reload()` → no deadlock;
  data published; no `SecretRefreshed` log / `Reloads` counter after the
  callback.
- In-flight reload across disposal, with two barriers: the fake fetch signals
  *cancellation observed* and then waits on a *release* barrier before
  throwing `OperationCanceledException`. Assert `Dispose()` returned while the
  fetch is still held, then release; the reload completes without throwing;
  `Data`/`CurrentVersionId` unchanged; no reload token, no
  `OnReloadException`, no failure log, no `reloads.failed` count; reload
  activity has a `disposed` event.
- Fetch that ignores cancellation and **succeeds** after disposal → not
  published. Fetch that fails with a non-cancellation exception after
  disposal → swallowed, no callback.
- Processor that disposes the provider before returning → not published.
- In-flight `Load()` across disposal → `ObjectDisposedException` without
  inner exception, no data, no `OnLoadException`.
- `Load()` whose change notification disposes the provider and whose logger
  then throws → `Load()` returns normally, data published.
- Logger factory whose `CreateLogger` throws during a reload → the overlap
  guard is reset (a subsequent reload runs).
- Activity listener whose `ActivityStarted` disposes the provider and throws
  during a reload → no exception escapes `Reload()`, the guard is reset.
- Activity listener whose `ActivityStopped` disposes the provider and throws
  → no exception escapes `Reload()`, the guard is reset; for `Load()`, a
  published load returns normally.
- Timeout too large for `CancelAfter` → load fails as before (enveloped), and
  repeating it does not accumulate registrations (assert via a subsequent
  `Dispose()` not observing stale callbacks — or, if not observable, cover
  by code review only).
- Replacing `source.ConfigurationWatcher` after the provider is built does not
  change which watcher is started or stopped.

Polling watcher (`SecretsManagerPollingWatcherShould.cs`):

- `StartWatching` after `Dispose()` / after `DisposeAsync()` throws
  `ObjectDisposedException`.
- `StopWatching()` after `Dispose()` is a no-op; `Dispose()` / `DisposeAsync()`
  idempotent.
- `Dispose()` while started → no further reloads after advancing time.
- `Dispose()` from inside the reload callback (before `Reschedule`) → no
  further reloads.
- Existing "ThrowWhenStartedTwice" stays green; two `StartWatching` calls
  released together from a barrier → exactly one succeeds, the other throws
  "already started".
- `StartWatching` and `Dispose()` released together from a barrier → either
  start throws `ObjectDisposedException`, or it succeeds and no reload ever
  fires after advancing time.

### Docs

- README "Refreshing secrets": short "Lifetime" note — disposing the
  configuration stops the watcher; ownership table from ADR-0019.
- ADR-0006 and ADR-0007: appended `Amendment (see ADR-0019)` notes.

### Risk

Behavioural, not API-breaking: adding `IDisposable` to a sealed class passes
package validation. Anyone who disposed a configuration root and still relied
on refresh loses it; caller-supplied watchers now get `StopWatching()` on
disposal. Release as a 3.x minor and state both in the release notes.
