# Features

New capabilities, ordered by value-to-effort. All are additive and shippable in
a 2.x minor unless noted.

---

## F1. Plain-text secret processor

**Status:** open · **Breaking:** no · **Effort:** S · **Priority:** high

### Why

A secret holding a single opaque value — a password, an API key, a connection
string — is an extremely common Secrets Manager pattern. Today it fails: the
JSON parser's bracket pre-check rejects it and
`SecretProcessor<T>.GetConfigurationData` throws
`FormatException("Secret '...' cannot be parsed, have you used appropriate secrets processor?")`.
Users must write a custom `ISecretProcessor` for the simplest possible case.

### Where

- `src/.../SecretProcessor.cs` (add to `SecretsProcessor`)
- new `src/.../PlainText/` or alongside existing processors

### Change

Add a processor mapping the whole secret string to a single configuration key:

```csharp
public static class SecretsProcessor
{
    public static readonly ISecretProcessor Json = /* existing */;
    public static readonly ISecretProcessor PlainText = new PlainTextSecretProcessor();
}
```

Semantics to settle and document:

- The key is `ConfigurationKeyPrefix` verbatim. An empty prefix is a
  configuration error — a value cannot live at the configuration root — so
  throw `InvalidOperationException` with a message telling the user to set a
  prefix.
- Key transformers still apply to that key (or, post-**A4**, the provider
  applies them). Consistency matters more than usefulness here.
- Trailing newlines are **not** trimmed. Secrets are byte-exact; trimming would
  be a silent data change.

Add a builder shortcut mirroring `WithJsonProcessor()`:

```csharp
public SecretsManagerConfigurationBuilder WithPlainTextProcessor();
```

### Tests

- `tests/.../SecretProcessorShould.cs` — value lands under the prefix; empty
  prefix throws; transformers applied; whitespace preserved.
- Integration test with a real non-JSON secret.

### Docs

README "Secret processing" section — this is the headline example.

---

## F2. Externally triggered reload

**Status:** open · **Breaking:** no · **Effort:** S · **Priority:** high

### Why

[ADR-0007](../adr/0007-refresh-via-pluggable-watcher.md) declines refresh by
default because every `GetSecretValue` is billed — polling costs money forever,
whether or not the secret changed. The cost-free alternative is reacting to
rotation events (EventBridge `AWS API Call via CloudTrail` /
`SecretRotationSucceeded`, or an SNS/SQS fan-out), but consumers cannot wire
that up today: `IConfigurationWatcher` has no "reload now" handle, and
`ISecretsManagerConfigurationProvider.Reload()` is only reachable by digging
through `IConfigurationRoot.Providers`.

This directly serves the ADR's own stated concern, so it needs no reversal of
that decision.

### Change

Ship a watcher that holds the provider reference and exposes a trigger:

```csharp
public sealed class ManualConfigurationWatcher : IConfigurationWatcher
{
    public void StartWatching(ISecretsManagerConfigurationProvider provider);
    public void StopWatching();

    /// <summary>Triggers reload. No-op if watching has not started.</summary>
    public void RequestReload();
}
```

Notes:

- `RequestReload()` must be safe to call from any thread and before
  `StartWatching` (no-op — the initial load has not succeeded yet, matching the
  existing rule that watching starts only after a successful load).
- It must **not** swallow exceptions from `Reload()`, consistent with the
  polling watcher. Callers handle failures via `OnReloadException`.
- The instance is created by the consumer, so they keep the reference and can
  register it in DI to call from a message handler.

Builder shortcut:

```csharp
public SecretsManagerConfigurationBuilder WithManualWatcher(ManualConfigurationWatcher watcher);
```

### Tests

- `RequestReload()` before `StartWatching` is a no-op.
- After `StartWatching`, it calls `Reload()` exactly once per invocation.
- Exceptions propagate to the caller.

### Docs

README "Refreshing secrets" — present it as the cost-free alternative to
polling, with a short SQS handler example.

---

## F3. Jitter for the polling watcher

**Status:** open · **Breaking:** no · **Effort:** S

### Why

Fifty pods rolled out by one deployment all start their timers within a second
of each other and then poll in lockstep every interval, producing a periodic
spike against the Secrets Manager rate limit. Self-inflicted throttling, easily
avoided.

### Where

`src/.../SecretsManagerPollingWatcher.cs`

### Change

Add an optional jitter to the constructor and to the builder shortcuts:

```csharp
public SecretsManagerPollingWatcher(TimeSpan interval, TimeSpan maxJitter);
public SecretsManagerPollingWatcher(TimeSpan interval, TimeSpan maxJitter, TimeProvider timeProvider);
```

Implementation:

- Apply jitter to the **initial due time** as well as to each period, otherwise
  the fleet stays synchronised after the first tick.
- A timer created with a fixed period cannot vary per tick, so switch to a
  one-shot timer that reschedules itself with a fresh jitter after each reload.
  Keep the existing "throws if started twice" guard.
- Use `Random.Shared`. Do not add a seedable RNG abstraction for testability —
  assert on bounds (next due time within `[interval, interval + maxJitter]`)
  using `FakeTimeProvider`, not on exact values.
- Default `maxJitter` to `TimeSpan.Zero` on existing constructors so current
  behaviour is unchanged.

### Tests

`tests/.../SecretsManagerPollingWatcherShould.cs` — with `FakeTimeProvider`,
assert reload happens within the jitter window and that rescheduling continues
indefinitely.

---

## F4. Activity tags

**Status:** done · **Breaking:** no · **Effort:** XS · **Priority:** high

### Why

The `Load` / `Reload` activities carry events and status but **no attributes at
all**. With more than one source configured, a trace shows several identical
spans and there is no way to tell which secret each belongs to. Cheapest
diagnostics improvement available.

### Where

`src/.../SecretsManagerConfigurationProvider.cs:49, 102`

### Change

```csharp
using var activity = ActivityDescriptors.Source.StartActivity(ActivityDescriptors.LoadActivityName);
activity?.SetTag("aws.secretsmanager.secret.id", secretName);
```

and after a successful fetch:

```csharp
activity?.SetTag("aws.secretsmanager.secret.version_id", secret.VersionId);
activity?.SetTag("aws.secretsmanager.secret.arn", secret.Arn); // full ARN from the response, when available
```

Per [ADR-0010](../adr/0010-diagnostics-and-late-bound-logging.md), secret
*values* must never be emitted. Names, ARNs and version ids are already logged,
so they are in scope. Do not add tags derived from the payload.

Prefer `StartActivity(name, ActivityKind.Internal, tags: ...)` if you want to
avoid the null-check dance, but note tags passed at start are visible to
sampling decisions, which is a plus.

### Tests

`tests/` — using `ActivityListener`, assert the tag is present on the recorded
activity.

### Docs

README "Diagnostics" — list the emitted tags.

---

## F5. Metrics via `System.Diagnostics.Metrics`

**Status:** done · **Breaking:** no · **Effort:** M

### Why

The natural production alert for this library is "a secret has not refreshed in
24 hours" or "reloads are failing", and neither is expressible today. `Meter`
is in-box (no new dependency, so ADR-0002 holds) and mirrors the existing
`ActivitySource` approach from ADR-0010.

### Change

Add to `Diagnostics/`:

```csharp
public static class MeterDescriptors
{
    public static readonly string MeterName = "W4k.Extensions.Configuration.Aws.SecretsManager";
}
```

Instruments, all tagged with `aws.secretsmanager.secret.id`:

| Instrument | Type | Meaning |
| --- | --- | --- |
| `w4k.secretsmanager.loads` | Counter | initial loads attempted |
| `w4k.secretsmanager.reloads` | Counter | reloads that changed data |
| `w4k.secretsmanager.reloads.skipped` | Counter | polls where version id was unchanged |
| `w4k.secretsmanager.loads.failed` | Counter | initial loads that failed |
| `w4k.secretsmanager.reloads.failed` | Counter | reloads that failed |

Constraints:

- One static `Meter` for the assembly, created once. No per-provider meters.
- Instruments must be created eagerly at type init, not per call.
- Zero cost when nobody is listening — that is inherent to `Meter`, but do not
  compute tag values before checking `instrument.Enabled` if the computation is
  non-trivial.
- This adds always-on work to the load path. Weigh against guiding principle 2
  ("what the consumer does not opt into, does not run") — counters on an
  unlistened `Meter` are effectively free, so it passes, but say so in the ADR.

Needs an ADR extending ADR-0010 to cover metrics.

### Tests

`MeterListener`-based assertions that each counter fires on the expected path.

---

## F6. Expose last-load state on the provider

**Status:** open · **Breaking:** no (additive) · **Effort:** XS
**Depends on:** pairs naturally with F5

### Why

There is no supported way to answer "did this secret ever load, and how stale is
it?" — needed for a health check or a readiness probe. The provider already
tracks the version id internally.

### Where

`src/.../SecretsManagerConfigurationProvider.cs`, and
`ISecretsManagerConfigurationProvider` if it should be on the interface.

### Change

```csharp
public string? CurrentVersionId { get; }
public DateTimeOffset? LastLoadedAt { get; }
```

- Set both in `SetData`. Use the source's `TimeProvider` if F3 introduces one on
  the source; otherwise `DateTimeOffset.UtcNow` is acceptable here.
- Apply the same `Volatile` treatment as **C3**.
- Adding members to the public `ISecretsManagerConfigurationProvider` interface
  is a **breaking change** for anyone implementing it. Put them on the concrete
  `SecretsManagerConfigurationProvider` only, unless this is bundled into v3.

### Docs

README — short example of an `IHealthCheck` iterating
`IConfigurationRoot.Providers.OfType<SecretsManagerConfigurationProvider>()`,
reusing the pattern already shown for late-bound logging.
