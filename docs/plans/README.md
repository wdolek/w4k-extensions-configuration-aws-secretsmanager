# Implementation plans

Backlog of reviewed, ready-to-pick-up work items for
`W4k.Extensions.Configuration.Aws.SecretsManager`.

Unlike [ADRs](../adr/README.md), which are immutable records of decisions already
made, these documents are **proposals**. A plan is deleted (or marked `Done`)
once implemented, and may be rejected outright.

## How to use these documents

Each plan file groups tasks by category. Every task is self-contained and
states:

- **Why** — the problem, with enough context to judge whether it is worth doing.
- **Where** — exact file (and line, at time of writing — verify before editing).
- **Change** — the concrete edit, usually with before/after code.
- **Tests** — what to add or fix.
- **Risk** — breaking-change classification and anything to watch out for.

Tasks are independent unless a `Depends on` note says otherwise. Do not batch
unrelated tasks into one commit.

## Categories

| File | Scope |
| --- | --- |
| [correctness.md](correctness.md) | Bugs and latent defects. Highest priority. |
| [api-surface.md](api-surface.md) | Public API shape, usability, deprecations. |
| [performance.md](performance.md) | Latency, allocations, AWS call count. |
| [features.md](features.md) | New capabilities. |
| [documentation.md](documentation.md) | README / XML doc gaps. Cheap wins. |

## Versioning notes

- The package targets `net8.0;net9.0;net10.0` and has
  `EnablePackageValidation` with `PackageValidationBaselineVersion=2.0.0`.
  Any public API removal or signature change needs either a major version bump
  or an entry in `CompatibilitySuppressions.xml` (which does not exist yet —
  create it via `dotnet pack /p:GenerateCompatibilitySuppressionFile=true`).
- Tasks tagged **`v3`** must not ship in a 2.x release.
- Behavioural changes that do not alter signatures will *not* be caught by
  package validation. Call them out in release notes explicitly.

## Verification

```pwsh
dotnet build W4k.Extensions.Configuration.Aws.SecretsManager.sln
dotnet test tests/W4k.Extensions.Configuration.Aws.SecretsManager.Tests
```

Integration tests hit real AWS and require the `w4ktest@admin` profile; see
`tests/W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests/README.md`.
Run them only when a task says to.
