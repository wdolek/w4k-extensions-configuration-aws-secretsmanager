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
