# 2. Minimal dependency footprint

- Status: Accepted
- Date: 2024-01

## Context

The library is loaded into every application that needs secrets, and it runs at
the very beginning of startup. Any dependency it pulls in becomes a dependency
of the host application, with the usual costs: version conflicts, larger
deployment artifacts, more surface for CVEs, and slower restore.

At the same time, the library needs to talk to AWS, produce configuration data,
and be observable.

## Decision

Depend on the smallest possible set of packages:

| Package | Reason |
| --- | --- |
| `AWSSDK.SecretsManager` | Unavoidable — the actual service client. Range `[4.0.3.1,5.0.0)`. |
| `Microsoft.Extensions.Configuration` | The provider base type (`ConfigurationProvider`) and `ConfigurationPath`. |
| `Microsoft.Extensions.Logging.Abstractions` | Abstractions only, never a logging implementation. |
| `Microsoft.SourceLink.GitHub` | Build-time only (`PrivateAssets=all`), not part of the package graph. |

Consequences of this rule in code:

- **No `AWSSDK.Extensions.NETCore.Setup` dependency.** The library never resolves
  an AWS client from DI; the client is supplied or default-constructed
  (see [ADR-0009](0009-secrets-manager-client-resolution.md)).
- **No `AWSSDK.SecretsManager.Caching` dependency.** Caching is not needed —
  the configuration provider _is_ the cache; the value is read once and held in
  `ConfigurationProvider.Data`.
- **No JSON library.** `System.Text.Json` from the shared framework is used
  (`JsonDocument`), not `Newtonsoft.Json`.
- **No `Microsoft.Extensions.Options` / DI dependency.** Binding is the
  consumer's concern.
- **`Microsoft.Extensions.*` references use open-ended ranges per TFM**
  (`[8.0.0,)`, `[9.0.0,)`, `[10.0.0,)`) so the library never forces a downgrade
  or an upgrade on the host application.

The same principle applies at runtime: nothing runs unless asked for. There is
no background thread, no timer and no polling unless a watcher is explicitly
configured ([ADR-0007](0007-refresh-via-pluggable-watcher.md)), and logging
defaults to `NullLoggerFactory.Instance`.

Allocation-level care follows from the same principle, e.g.
`CollectionsMarshal.AsSpan` when iterating key transformers, and returning a
cloned `JsonElement` instead of keeping the whole `JsonDocument` alive.

## Consequences

- Trivially droppable into existing applications; low risk of diamond
  dependency conflicts.
- Some convenience is left to the consumer — for example, wiring an
  `IAmazonSecretsManager` created from `GetAWSOptions()` is a manual step
  (shown in `sample/SecretApi`).
- Features that would require a new dependency must be delivered through an
  extension point instead of being built in
  ([ADR-0003](0003-pluggable-secret-processing.md),
  [ADR-0007](0007-refresh-via-pluggable-watcher.md)).
- Any proposed new package reference needs an ADR of its own.
