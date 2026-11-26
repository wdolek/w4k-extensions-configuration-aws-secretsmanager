# 20. Validate the package against the latest release

- Status: Accepted
- Date: 2026-11

## Context

[ADR-0011](0011-target-frameworks-and-api-compatibility.md) enforces API
compatibility with `EnablePackageValidation` against a released
`PackageValidationBaselineVersion`, raised at each major release.
[ADR-0018](0018-target-frameworks-v3.md) accordingly set it to `2.4.0`, the
last 2.x release, when 3.0.0 dropped the `net8.0` and `net9.0` targets.

Validating 3.x against 2.4.0 protects only the API that already shipped in
2.4.0. Anything first shipped in 3.0.0 could be removed or changed in a 3.x
minor without the build failing. The 2.4.0 baseline also needs
`CompatibilitySuppressions.xml`, shipped in the package, to acknowledge the
`PKV006` diagnostics for the two dropped targets.

## Decision

- **The baseline is the most recently published version** — the release
  immediately before the one being prepared. It is raised together with
  `VersionPrefix` for every release, not only at majors. 3.1.0 validates
  against 3.0.0.
- **Delete `CompatibilitySuppressions.xml`** and stop packing it. Against
  3.0.0 (`net10.0`, `net11.0`) there is nothing to suppress.
- A major release that breaks the API on purpose still validates against the
  last release of the previous major, with suppressions for exactly the
  intended breaks (`dotnet pack /p:GenerateCompatibilitySuppressionFile=true`),
  as ADR-0018 did for 3.0.0. Once that major is published, the baseline moves
  to it and the suppressions go away.

## Consequences

- Every public API that has shipped in the current major is protected:
  removing or changing it fails the build.
- Each version bump has a second step — raising the baseline to the previous
  release — and the baseline version must already be published on NuGet,
  since restore downloads it.
- Validation compares against one version only. That is sufficient because
  each release was itself validated against its predecessor.

## Related

- [ADR-0011](0011-target-frameworks-and-api-compatibility.md) — introduces
  package validation and the baseline; this ADR changes when it is raised.
- [ADR-0018](0018-target-frameworks-v3.md) — set the 2.4.0 baseline and the
  `PKV006` suppressions this ADR replaces.
