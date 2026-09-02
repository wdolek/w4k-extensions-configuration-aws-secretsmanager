# 10. Diagnostics and late-bound logging

- Status: Accepted
- Date: 2024-03

## Context

"The application won't start and the secret is empty" is the single most common
support question for a library like this. Secret values must never be logged, so
observability has to come from metadata: which secret, which version, how long it
took, what failed.

The hard constraint is timing. Configuration is loaded during
`ConfigurationBuilder.Build()`, before the host is built, so at load time there
is no `ILoggerFactory` from DI and no OpenTelemetry `TracerProvider` registered.
Reload, by contrast, happens long after the host is running, when both exist.

Adding a logging implementation dependency was rejected
([ADR-0002](0002-minimal-dependency-footprint.md)).

## Decision

**Tracing** via `System.Diagnostics.ActivitySource` — a shared-framework
primitive with no package cost and native OpenTelemetry support:

- Source name `W4k.Extensions.Configuration.Aws.SecretsManager`, exposed as
  `ActivityDescriptors.ActivitySourceName` so consumers can call
  `AddSource(...)` without a magic string.
- Activities `W4k.SecretsManager.Load` and `W4k.SecretsManager.Reload`, with
  events `loaded`, `reloaded`, `skipped` and status `Ok`/`Error`.
- Exception recording is TFM-conditional: `Activity.AddException` on
  net9.0+, and a hand-rolled OTel-conventional event
  (`exception` / `exception.message` / `exception.type`) on net8.0.
- `ActivityListenerExtensions.ListenToSecretsManagerActivitySource(...)` is
  provided for the load phase, when no OTel pipeline exists yet. The README
  documents that only `Reload` normally reaches an OTel exporter.

**Logging** via `Microsoft.Extensions.Logging.Abstractions` only:

- `source.LoggerFactory` defaults to `NullLoggerFactory.Instance` — no logging
  cost when not configured.
- Messages use source-generated `[LoggerMessage]` (no boxing, no allocation when
  the level is disabled), with stable event ids and names.
- **The logger is created on every `Load()`/`Reload()` call** rather than cached
  in the constructor. This is deliberate: it lets a consumer swap in a real
  `ILoggerFactory` after the host is built —

  ```csharp
  foreach (var p in configRoot.Providers.OfType<SecretsManagerConfigurationProvider>())
      p.Source.LoggerFactory = loggerFactory;
  ```

  — so reload logging lands in the application's normal logging pipeline.

Log and trace payloads carry secret **name** and **version id** only, never the
value.

> **Amendment (see [ADR-0016](0016-do-not-tag-secrets-with-their-arn.md)):** an
> earlier revision also tagged the secret's ARN, which embeds the AWS account
> id and therefore violated the "name and version id only" rule above. ADR-0016
> removes the ARN tag and records it as a permanent constraint, not an
> oversight.

## Consequences

- Refresh behaviour is observable in standard OTel tooling with one `AddSource`
  call.
- Startup-time diagnostics require the explicit listener; this asymmetry is
  inherent to loading before the host exists and is documented rather than
  hidden.
- `ILoggerFactory.CreateLogger<T>()` per call is a small, cached-by-implementation
  cost on operations that already perform a network round trip — negligible, and
  it buys late binding.
- `Source.LoggerFactory` being publicly settable after `Build()` is intentional
  mutable state, and the only supported way to retrofit logging.
- The `ActivitySource` version string must be kept in step with the package
  version.
