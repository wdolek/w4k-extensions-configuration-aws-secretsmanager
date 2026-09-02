# Features

New capabilities, ordered by value-to-effort. All are additive and shippable in
a 2.x minor unless noted.

---

## F1. Plain-text secret processor

**Status:** done · **Breaking:** no · **Effort:** S · **Priority:** high

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

Add `PlainTextSecretProcessor` with an *optional explicit configuration key*:

```csharp
public sealed class PlainTextSecretProcessor : ISecretProcessor
{
    /// <summary>Key is <see cref="SecretsManagerConfigurationSource.ConfigurationKeyPrefix"/> verbatim.</summary>
    public PlainTextSecretProcessor();

    /// <summary>Key is <c>{prefix}:{configurationKey}</c>.</summary>
    public PlainTextSecretProcessor(string configurationKey);
}
```

and register it alongside JSON:

```csharp
public static class SecretsProcessor
{
    public static readonly ISecretProcessor Json = /* existing */;
    public static readonly ISecretProcessor PlainText = new PlainTextSecretProcessor();
}
```

Key semantics (settled):

- **Explicit key given** — the value lands under
  `{ConfigurationKeyPrefix}:{configurationKey}`, composed with
  `ConfigurationPath.KeyDelimiter` exactly like object keys in the JSON
  tokenizer (`JsonElementTokenizer.ComposeKey`). An empty prefix is fine; the
  key then stands alone. A whitespace-only key is rejected with
  `ArgumentException` at construction.
- **No explicit key** — the key is `ConfigurationKeyPrefix` verbatim. An empty
  prefix is a configuration error — a value cannot live at the configuration
  root — so throw `InvalidOperationException` at processing time, with a
  message telling the user to set a prefix or pass an explicit key.
- Key transformers still apply to the final composed key (or, post-**A4**, the
  provider applies them). Consistency matters more than usefulness here.
- Trailing newlines are **not** trimmed. Secrets are byte-exact; trimming would
  be a silent data change.

Why both modes: prefix-as-key is the terse one-secret-one-value setup; the
explicit key decouples the configuration key from the secret name (which is
often a path like `prod/myapp/stripe`, unusable as a config key) and works
when no prefix is set. Composing the prefix in front of the explicit key keeps
one mental model — *the prefix namespaces every key this secret produces* —
identical to JSON tokenization.

Builder shortcuts mirroring `WithJsonProcessor()`:

```csharp
public SecretsManagerConfigurationBuilder WithPlainTextProcessor();
public SecretsManagerConfigurationBuilder WithPlainTextProcessor(string configurationKey);
```

### Tests

- `tests/.../SecretProcessorShould.cs` — value under explicit key with and
  without prefix; parameterless processor uses the prefix as key; neither key
  nor prefix set throws; whitespace-only explicit key throws at construction;
  transformers applied; trailing whitespace preserved.
- Integration test with a real non-JSON secret.

### Docs

README "Secret processing" section — this is the headline example, showing
both the explicit-key and prefix-as-key modes.

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

**Status:** done · **Breaking:** no · **Effort:** S

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
```

Per [ADR-0010](../adr/0010-diagnostics-and-late-bound-logging.md), secret
*values* must never be emitted, and payloads carry secret **name** and
**version id** only. Do not add tags derived from the payload.

> **Amendment ([ADR-0016](../adr/0016-do-not-tag-secrets-with-their-arn.md)):**
> an earlier revision of this feature also tagged the secret's full ARN
> (`aws.secretsmanager.secret.arn`, populated from the fetch response). That
> was removed before release: an ARN embeds the AWS account id
> (`arn:aws:secretsmanager:{region}:{account-id}:secret:{name}`), and traces
> routinely leave the process boundary (OTel exporters, SaaS observability
> backends), which is exactly the leak ADR-0010's "name and version id only"
> rule exists to prevent. The configured `secret.id` tag (name or ARN, as the
> consumer supplied it) is sufficient to correlate telemetry. ADR-0016 records
> this as a permanent constraint so it is not silently reintroduced.

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

**Status:** done · **Breaking:** no (additive) · **Effort:** XS
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

---

## F7. Dotenv (`KEY=VALUE`) secret processor

**Status:** open · **Breaking:** no · **Effort:** S

### Why

Secrets pasted from `.env` files or generated by tooling (`docker run
--env-file`, Terraform `aws_secretsmanager_secret`, CI variable dumps) are
`KEY=VALUE` lines, not JSON. Today that means a custom `ISecretProcessor` for
a format with three rules.

### Where

- `src/.../SecretProcessor.cs` (add to `SecretsProcessor`)
- new `src/.../DotEnv/` following the `Json/` layout

### Change

`DotEnvSecretProcessor`, composed like the JSON one:

```csharp
public static class SecretsProcessor
{
    public static readonly ISecretProcessor DotEnv =
        new SecretProcessor<IReadOnlyList<KeyValuePair<string, string?>>>(
            new DotEnvParser(),
            new DotEnvTokenizer());
}
```

Parsing rules (keep deliberately small — this is dotenv, not a shell):

- one `KEY=VALUE` per line; split on the **first** `=`
- blank lines and lines starting with `#` (after optional leading
  whitespace) are skipped
- optional `export ` prefix is stripped (secrets are frequently copied out of
  shell profiles)
- the key is trimmed; a line with no `=` or an empty key is a `FormatException`
  (the standard "cannot be parsed" message)
- if the value is wrapped in matching single or double quotes, strip them;
  otherwise take it verbatim — **no escape-sequence processing, no trimming**
- later assignments overwrite earlier ones (consistent with the
  `OrdinalIgnoreCase` last-write-wins dictionary)
- keys compose with `ConfigurationKeyPrefix` like JSON object properties;
  key transformers apply as usual
- no multiline values

Builder shortcut: `WithDotEnvProcessor()`.

### Tests

`tests/.../SecretProcessorShould.cs` — comments, `export`, quoted and
unquoted values, empty values, duplicate keys, missing `=` throws, prefix
composition, transformers.

### Docs

README "Secret processing" — add to the processor table once it exists
(depends on D-items covering processors).

---

## F8. Base64 secret processor

**Status:** open · **Breaking:** no · **Effort:** S

### Why

Two cases:

1. A string secret whose *content* is base64 — API keys provisioned by
   services that hand out base64 blobs, or JSON deliberately stored encoded.
2. A binary secret (certificate, signing key, p12) — **blocked for now**: the
   pipeline only carries strings, and `SecretFetcher` UTF-8-decodes
   `SecretBinary` before any processor runs. For non-UTF-8 payloads that
   decode is lossy — see C7. Raw-byte access for processors is v3 material
   and should ride along with **A4**'s contract change (extend
   `SecretProcessingContext` with the payload as `ReadOnlyMemory<byte>` and
   let a processor opt in).

This item delivers case 1 and the type that case 2 will reuse.

### Where

- `src/.../SecretProcessor.cs` (add to `SecretsProcessor`)
- new `src/.../Base64/` or alongside `SecretProcessor.cs`

### Change

```csharp
public sealed class Base64SecretProcessor : ISecretProcessor
{
    /// <summary>Decodes and emits a single value; key semantics as F1.</summary>
    public Base64SecretProcessor();
    public Base64SecretProcessor(string configurationKey);

    /// <summary>Decodes, then hands the decoded string to <paramref name="innerProcessor"/>.</summary>
    public Base64SecretProcessor(ISecretProcessor innerProcessor);
}
```

- Decoding is `Convert.FromBase64String` (tolerates embedded whitespace) plus
  UTF-8; key semantics for the single-value form mirror F1 exactly (optional
  explicit key, prefix composition, transformers apply).
- The inner-processor form covers base64-wrapped JSON —
  `new Base64SecretProcessor(SecretsProcessor.Json)` — and passes the source
  through unchanged.
- Invalid base64 fails with the standard `FormatException` "cannot be parsed"
  message.

Builder shortcuts: `WithBase64Processor()`,
`WithBase64Processor(string configurationKey)`; the inner-processor form is
reachable via `WithProcessor(...)` — no dedicated shortcut.

### Tests

- Round-trip: base64 value lands under the expected key (all key modes).
- Invalid base64 → `FormatException` with secret name in message.
- Inner-processor delegation: base64-wrapped JSON tokenizes as JSON.
- Empty payload → `FormatException` (no key to emit, same as F1 with no
  prefix).

---

## F9. Fallback (composite) secret processor

**Status:** open · **Breaking:** no · **Effort:** S · **Priority:** low

### Why

Fleets where most secrets are JSON but a few are single values: users want
one `AddSecretsManager` registration without per-secret processors. The
composite must be **explicit opt-in** — implicit format sniffing as a default
is a trap (a malformed JSON secret would silently become one config value).

### Change

```csharp
public sealed class FallbackSecretProcessor(params ISecretProcessor[] processors)
    : ISecretProcessor;
```

- Tries processors in order; the first that does not throw wins.
- If all fail, the **last** exception propagates.
- **No preset, no builder shortcut** — the point is that the user writes the
  chain themselves:
  `WithProcessor(new FallbackSecretProcessor(SecretsProcessor.Json, SecretsProcessor.PlainText))`.
- Document loudly: plain text always parses, so it must be **last**; anything
  after it is dead code, and a broken JSON secret silently degrades to a
  single value instead of failing.

### Tests

- First-processor success short-circuits.
- First throws, second succeeds.
- All throw → last exception surfaced.
- JSON-then-PlainText ordering works end to end through a provider.

---

## Out of scope (recorded so we stop re-litigating)

- **YAML** — needs YamlDotNet, which conflicts with ADR-0002's
  no-additional-dependencies stance. If real demand appears, ship a companion
  package (e.g. `W4k.Extensions.Configuration.Aws.SecretsManager.Yaml`)
  implementing the same processor contracts.
- **XML** — in-box via `System.Xml`, but little demand for XML secrets;
  revisit if asked for.
