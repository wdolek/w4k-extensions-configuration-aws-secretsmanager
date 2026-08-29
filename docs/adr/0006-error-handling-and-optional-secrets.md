# 6. Error handling and optional secrets

- Status: Accepted
- Date: 2024-01

## Context

Fetching a secret can fail for many reasons: the secret does not exist, the
caller lacks IAM permissions, the credentials chain is empty, the network is
unreachable, the payload cannot be parsed, or the call exceeds the timeout.

Failure severity depends entirely on the deployment, not on the library:

- In production, a missing secret is usually fatal and the application must not
  start with half-configured credentials.
- Locally, or in tests, the same secret may legitimately be absent and
  overridden by user secrets or environment variables.

The AWS SDK throws a wide range of exception types, none of which a consumer
should be forced to catch by type.

## Decision

**Envelope all failures.** Both `Load()` and `Reload()` catch every exception,
log it, record it on the activity, and rethrow it wrapped in
`SecretRetrievalException` with the original as `InnerException`. Rethrow uses
`ExceptionDispatchInfo`, and the helper is marked `[StackTraceHidden]` so the
stack trace points at the real failure site.

**Make severity a callback, not a flag.** The source exposes
`OnLoadException` and `OnReloadException`, each receiving a
`SecretsManagerExceptionContext` with `Provider`, `Exception` and a settable
`Ignore`:

```csharp
source.OnLoadException(ctx => ctx.Ignore = ctx.Exception is ResourceNotFoundException);
```

**`isOptional: true` is sugar over that callback** — the extension methods simply
register a handler that sets `Ignore = true` on both callbacks. There is no
separate "optional" concept in the provider.

Default behaviour is to throw, i.e. fail fast at startup.

## Consequences

- One exception type to catch, with the SDK exception preserved for inspection.
- Consumers can be selective ("ignore not-found, but fail on access denied")
  instead of choosing between all-or-nothing.
- An ignored load leaves the provider with empty data; the application starts
  and binding/validation decides what happens next. This is the intended
  fail-fast handoff to `ValidateOnStart()`.
- Reload failures are surfaced the same way. Because the polling watcher does not
  swallow exceptions ([ADR-0007](0007-refresh-via-pluggable-watcher.md)),
  configuring `OnReloadException` is effectively required when using a watcher.
- Wrapping means callers cannot use exception filters on the SDK type at the
  catch site without unwrapping `InnerException`.
