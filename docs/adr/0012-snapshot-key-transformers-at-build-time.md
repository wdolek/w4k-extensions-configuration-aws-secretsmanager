# 12. Snapshot key transformers at build time

- Status: Accepted
- Date: 2026-08

## Context

`SecretsManagerConfigurationSource.KeyTransformers` is a live `List<>` shared
with the provider ([ADR-0005](0005-configuration-key-transformers.md)).
A consumer mutating it after the host was built changed reload behaviour
mid-flight, and mutating it concurrently with a watcher-driven reload raced
the span-based iteration in `SecretProcessor<T>`.

The list must stay publicly mutable *before* build: clearing or extending it is
a supported configuration style.

## Decision

`SecretsManagerConfigurationSource.Build()` copies `KeyTransformers` into an
array and secret processing reads that snapshot. Mutating the source list
after `Build()` has no effect.

## Consequences

- Reload behaviour is stable for the provider's lifetime and the data race
  against the transformers list is eliminated.
- All transformer configuration must happen before `Build()` — which is when
  configuration is expected to happen.
- Providers constructed directly instead of via `Build()` keep reading the
  live list; in practice this affects only unit tests.
- The allocation-free iteration from
  [ADR-0005](0005-configuration-key-transformers.md) is preserved: the
  snapshot is iterated as a span.
