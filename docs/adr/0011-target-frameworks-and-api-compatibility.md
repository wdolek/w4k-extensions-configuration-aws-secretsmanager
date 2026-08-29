# 11. Target frameworks and API compatibility

- Status: Accepted
- Date: 2024-01

## Context

The library sits at the very bottom of an application's startup path, so a
version conflict here is disruptive. Two related questions had to be settled:
which runtimes to support, and how strictly to guarantee that upgrading the
package does not break consumers.

Supporting `netstandard2.0` was considered. It would cost the modern APIs the
implementation relies on — `TimeProvider`, `CollectionsMarshal.AsSpan`,
`ArgumentException.ThrowIfNullOrWhiteSpace`, `[LoggerMessage]` source
generation, nullable reference types, `JsonDocument` behaviour — in exchange for
supporting runtimes that are out of support anyway.

## Decision

- **Target only supported LTS/STS .NET versions:** `net8.0`, `net9.0`,
  `net10.0`. No `netstandard2.0`, no .NET Framework.
- **Multi-target rather than lowest-common-denominator**, so newer runtimes get
  their better APIs. Currently the only divergence is `Activity.AddException`
  behind `#if NET9_0_OR_GREATER`
  ([ADR-0010](0010-diagnostics-and-late-bound-logging.md)).
- **Version-range references per TFM** for `Microsoft.Extensions.*`
  (`[8.0.0,)` / `[9.0.0,)` / `[10.0.0,)`) so the host application controls the
  actual version, and `[4.0.3.1,5.0.0)` for `AWSSDK.SecretsManager` to prevent an
  automatic jump across an SDK major version.
- **Enforce API compatibility in the build:** `EnablePackageValidation=true`
  with `PackageValidationBaselineVersion` set to a released version (currently
  `2.0.0`). Breaking the public surface fails the build; intentional exceptions
  are recorded in `CompatibilitySuppressions.xml`.
- **Ship a complete package:** XML documentation
  (`GenerateDocumentationFile`), deterministic builds, SourceLink and `snupkg`
  symbols for step-through debugging.
- **Enforce code style in the build** (`EnforceCodeStyleInBuild`,
  `AnalysisLevel=latest-Recommended`, extensive `.editorconfig`).

## Consequences

- Modern APIs are used freely, keeping the implementation small and
  allocation-conscious ([ADR-0002](0002-minimal-dependency-footprint.md)).
- Applications on .NET Framework or `netstandard2.0` cannot use the package.
- Dropping an out-of-support TFM is itself a breaking change and needs a major
  version bump.
- Every public type is API surface under package validation — a strong reason to
  keep the abstractions in `Abstractions/` few and narrow.
- The compatibility baseline must be raised deliberately at each major release.
- Test projects target only the latest TFM, so behaviour on older TFMs is
  compile-verified but not test-verified; the `#if` branches are the risk area.
- Trimming/AOT annotations (`IsTrimmable`, `IsAotCompatible`) are not set,
  mainly because the AWS SDK is not trim-friendly. Revisit if that changes.
