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
| [0012](0012-snapshot-key-transformers-at-build-time.md) | Snapshot key transformers at build time | Accepted |
| [0013](0013-metrics.md) | Metrics | Accepted |
| [0014](0014-binary-secrets-must-be-valid-utf8.md) | Binary secrets must be valid UTF-8 | Accepted |
| [0015](0015-built-in-plain-text-secret-processor.md) | Built-in plain text secret processor | Accepted |
| [0016](0016-do-not-tag-secrets-with-their-arn.md) | Do not tag secrets with their ARN | Accepted |
| [0017](0017-binary-secret-decode-failures-omit-the-payload.md) | Binary secret decode failures omit the payload | Accepted |

## Format

[MADR](https://adr.github.io/madr/)-lite: **Context**, **Decision**,
**Consequences**. Files are named `NNNN-kebab-case-title.md`. Records are
immutable — to reverse a decision, add a new ADR and mark the old one
`Superseded by ADR-NNNN`.

Two lighter-weight notes are allowed on an otherwise-immutable ADR, both as an
**appended** blockquote only — never edit or delete the original prose they
refer to, and never change `Status` for either:

- `> **Amendment (see ADR-NNNN):**` — a later ADR narrowed or partially
  changed *this* ADR's own decision, without replacing it outright. If the
  later ADR replaces the decision entirely instead of narrowing it, that is a
  full reversal: use `Superseded by ADR-NNNN` above, not this note.
- `> **Correction (see ADR-NNNN):**` — a detail in this ADR's Context or
  Consequences (not its core Decision) was made inaccurate by a later,
  otherwise-unrelated ADR — e.g. a passing description of implementation
  mechanics, or a claim like "requires a custom processor" that held until a
  later ADR added a built-in alternative. This ADR's own decision is
  unaffected; only a detail it mentioned in passing is now stale.

Both notes exist so a reader who opens an old ADR is one line away from
today's reality, without ever needing to reconstruct it from `git log`.
