# Architecture Decision Records

This directory records the significant design decisions behind
`W4k.Extensions.Configuration.Aws.SecretsManager`.

Records are written retrospectively, based on the current implementation (v2.x),
and describe _why_ things are the way they are.

## Guiding principles

Three constraints shape almost every decision in this library:

1. **It is a `Microsoft.Extensions.Configuration` provider.** The configuration
   pipeline is synchronous and runs before the host (and DI container) exists.
   The library must fit that model rather than fight it.
2. **Minimal footprint.** No hidden background work, no caches, no allocations
   that are not needed. What the consumer does not opt into, does not run.
3. **Minimal dependencies.** AWS SDK for Secrets Manager plus the configuration
   and logging _abstractions_. Nothing else.

## Index

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-configuration-source-with-synchronous-load.md) | Configuration source with synchronous load | Accepted |
| [0002](0002-minimal-dependency-footprint.md) | Minimal dependency footprint | Accepted |
| [0003](0003-pluggable-secret-processing.md) | Pluggable secret processing | Accepted |
| [0004](0004-json-flattening-semantics.md) | JSON flattening semantics | Accepted |
| [0005](0005-configuration-key-transformers.md) | Configuration key transformers | Accepted |
| [0006](0006-error-handling-and-optional-secrets.md) | Error handling and optional secrets | Accepted |
| [0007](0007-refresh-via-pluggable-watcher.md) | Refresh via pluggable watcher | Accepted |
| [0008](0008-change-detection-by-version-id.md) | Change detection by version id | Accepted |
| [0009](0009-secrets-manager-client-resolution.md) | Secrets Manager client resolution | Accepted |
| [0010](0010-diagnostics-and-late-bound-logging.md) | Diagnostics and late-bound logging | Accepted |
| [0011](0011-target-frameworks-and-api-compatibility.md) | Target frameworks and API compatibility | Accepted |

## Format

[MADR](https://adr.github.io/madr/)-lite: **Context**, **Decision**,
**Consequences**. Files are named `NNNN-kebab-case-title.md`. Records are
immutable — to reverse a decision, add a new ADR and mark the old one
`Superseded by ADR-NNNN`.
