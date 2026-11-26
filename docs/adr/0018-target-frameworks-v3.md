# 18. Target frameworks for v3: net10.0 and net11.0

- Status: Accepted
- Date: 2026-11

## Context

[ADR-0011](0011-target-frameworks-and-api-compatibility.md) targeted every
then-supported .NET release: `net8.0`, `net9.0`, `net10.0`. Both of the older
two are now out of support: .NET 8 (LTS) reached end of support on
2026-11-10, and .NET 9 (STS) reached end of support in May 2026.

.NET 11 (STS, released 2026-11-10, supported until November 2028) joins the
supported set alongside .NET 10 (LTS, supported until November 2028).

The only TFM-conditional code in the library was the exception-recording
branch in `SecretsManagerConfigurationProvider`
([ADR-0010](0010-diagnostics-and-late-bound-logging.md)): `Activity.AddException`
behind `#if NET9_0_OR_GREATER`, with a hand-rolled OTel-conventional
`ActivityEvent` fallback for `net8.0`. With `net10.0` as the floor, that
condition is always true.

## Decision

- **Target `net10.0;net11.0`** — the two supported LTS/STS runtimes. This
  amends the target list in
  [ADR-0011](0011-target-frameworks-and-api-compatibility.md); everything
  else in that ADR (multi-targeting over lowest-common-denominator,
  per-TFM version-range references, package validation, complete package)
  still stands.
- **Bump the major version to 3.0.0.** Dropping out-of-support TFMs is a
  breaking change per ADR-0011, so it cannot ship in a 2.x release.
- **Raise `PackageValidationBaselineVersion` to `2.4.0`**, the last 2.x
  release, as ADR-0011 prescribes for each major release.
- **Delete the `#if NET9_0_OR_GREATER` fallback entirely.** Exception
  recording now uses `Activity.AddException` unconditionally; the
  `ExceptionExtensions.ToActivityEvent` fallback is removed with the
  `net8.0` target. This amends the corresponding decision in
  [ADR-0010](0010-diagnostics-and-late-bound-logging.md).
- **Version-range references per TFM** for `Microsoft.Extensions.*` are now
  `[10.0.0,)` (net10.0) and `[11.0.0,)` (net11.0); `AWSSDK.SecretsManager`
  stays on `[4.0.3.1,5.0.0)`.
- **Test projects target `net11.0` only**, continuing the ADR-0011 practice
  of testing only the latest TFM; behaviour on `net10.0` is compile-verified
  only, and there are no `#if` branches left to diverge.

## Consequences

- Applications still on .NET 8 or .NET 9 must stay on the 2.x package line
  (2.4.0 is the last one supporting `net8.0`/`net9.0`).
- The library can freely use C# 14 features on all TFMs, since the lowest
  target compiles with C# 14 by default — `SecretsManagerConfigurationSource.Timeout`
  now uses the C# 14 `field` keyword instead of a hand-rolled backing field
  (the only such candidate; remaining private fields are either
  constructor-assigned `readonly`, where `field` is not accessible, or passed
  by `ref` to `Interlocked`/`Volatile`, where a real field is required).
  C# 15 / .NET 11-only APIs must stay behind `#if NET11_0_OR_GREATER` or
  `net11.0`-only code paths, and remain compile-verified only.
- With no TFM-conditional code left, the compile-verified-but-not-tested TFM
  risk called out in ADR-0011 disappears until a new divergence is
  introduced.
- New .NET 11 opportunities (C# 15 union types for the secret payload,
  `System.Text.Json` union support, native async runtime — currently
  preview) were evaluated and deliberately deferred: none of them justify
  a public API change or a preview feature flag in this release.
