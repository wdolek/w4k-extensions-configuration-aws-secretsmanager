# 8. Change detection by version id

- Status: Accepted
- Date: 2024-02

## Context

Every reload triggered by a watcher ([ADR-0007](0007-refresh-via-pluggable-watcher.md))
fetches the secret again. Most of the time the value has not changed — a five
minute poll against a secret rotated monthly is a no-op in the overwhelming
majority of cases.

Calling `OnReload()` unconditionally would fire the configuration change token on
every poll. Downstream that means `IOptionsMonitor` change callbacks running,
`IOptionsSnapshot` caches invalidating, and any consumer-registered reload
handler executing — repeatedly, for no reason.

Detecting change by comparing the payload would require holding the previous
secret value (or a hash of it) in memory, which is undesirable for secret
material.

## Decision

Use the AWS-assigned `VersionId` as the change signal.

- `SecretFetcher` returns `SecretValue { VersionId, Value }` from the
  `GetSecretValue` response.
- The provider stores `_currentSecretVersionId` on every successful `SetData`.
- `Reload()` compares ordinal-equal and short-circuits when unchanged:

```csharp
if (string.Equals(secret.VersionId, _currentSecretVersionId, StringComparison.Ordinal))
{
    // activity event "skipped", log SecretAlreadyLoaded
    return;
}
```

No data is replaced and `OnReload()` is not called in that case. `SetData` is the
single place where both the version id and `Data` are swapped and the change
token is raised, keeping the two in sync.

## Consequences

- Change tokens fire only on an actual new secret version. Both the "fires on new
  version" and "does not fire on same version" cases are covered by unit tests.
- No secret value or hash is retained for comparison purposes — only an opaque
  version identifier.
- The comparison is free; the AWS call is still made, so this reduces downstream
  churn, not cost.
- Version id is authoritative even if the new version's content is byte-identical
  to the previous one: that will be treated as a change. Harmless and preferable
  to comparing values.
- When a source is pinned to a fixed `VersionId`
  (`WithVersion(versionId: ...)`), reload can never observe a change; pinning to
  a `VersionStage` such as `AWSCURRENT` is the meaningful combination with a
  watcher.
