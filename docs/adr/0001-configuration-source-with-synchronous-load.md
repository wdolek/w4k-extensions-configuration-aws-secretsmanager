# 1. Configuration source with synchronous load

- Status: Accepted
- Date: 2024-01

## Context

The goal is to make AWS Secrets Manager secrets available through the standard
.NET configuration stack, so that consumers can bind them with the Options
pattern (`IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>`) exactly like
any other configuration value.

`Microsoft.Extensions.Configuration` offers one extension point for this:
`IConfigurationSource` + `ConfigurationProvider`. Its contract is
`void Load()` — **synchronous, with no async counterpart**. Providers are built
and loaded by `ConfigurationBuilder.Build()`, which happens before the host is
built and therefore before dependency injection, logging or hosted services
exist.

The AWS SDK exposes only `GetSecretValueAsync`. There is no supported
synchronous API.

Alternatives considered:

- **Load secrets outside the configuration system** (e.g. a hosted service that
  writes into an in-memory store). Loses `IConfiguration` composition, ordering
  and binding semantics; secrets would not be available during startup
  validation.
- **Expose an async API and require the caller to await before `Build()`.**
  Would not integrate with `IConfigurationBuilder` chaining, and every consumer
  would need bespoke wiring.

## Decision

Implement the library as a regular configuration source
(`SecretsManagerConfigurationSource`) and provider
(`SecretsManagerConfigurationProvider`), and block on the asynchronous SDK call
inside `Load()`:

```csharp
var cts = new CancellationTokenSource(Source.Timeout);
var secret = _secretFetcher.GetSecret(secretName, secretVersion, cts.Token)
    .ConfigureAwait(false)
    .GetAwaiter()
    .GetResult();
```

Two mitigations are mandatory for this sync-over-async pattern:

- `ConfigureAwait(false)` — do not capture the ambient synchronization context,
  which is the classic cause of deadlocks in sync-over-async code.
- A bounded timeout (`SecretsManagerConfigurationSource.Timeout`, default
  30 seconds) so that a hanging network call cannot stall application startup
  indefinitely.

Configuration happens through `IConfigurationBuilder` extension methods
(`AddSecretsManager(...)`), keeping the usage identical to `AddJsonFile` and
friends.

## Consequences

- Secrets are just configuration: prefix, binding, validation, change tokens and
  provider ordering all work without special cases.
- Secret values are available during startup, so
  `ValidateDataAnnotations().ValidateOnStart()` can fail fast.
- Application startup blocks a thread for the duration of the AWS call. This is
  accepted: it happens once per source at startup, on the startup thread, and is
  bounded by `Timeout`.
- Startup latency scales linearly with the number of secret sources; there is no
  parallel prefetch.
- Because loading precedes host construction, neither DI-resolved loggers nor
  DI-registered tracing are available at that moment — see
  [ADR-0010](0010-diagnostics-and-late-bound-logging.md).
- If .NET ever adds an asynchronous configuration provider contract, this
  decision should be revisited.
