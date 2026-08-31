# 13. Metrics

- Status: Accepted
- Date: 2026-08

## Context

[ADR-0010](0010-diagnostics-and-late-bound-logging.md) established tracing and
logging, but traces and logs are poor vehicles for the natural production
alerts of this library — "a secret has not refreshed in 24 hours" or "reloads
are failing". Those need counters, i.e. metrics.

`System.Diagnostics.Metrics.Meter` is in the shared framework, so
[ADR-0002](0002-minimal-dependency-footprint.md) holds: no new dependency, and
native OpenTelemetry support via `AddMeter(...)`, mirroring the existing
`ActivitySource` approach.

## Decision

A single static `Meter` for the assembly, named
`W4k.Extensions.Configuration.Aws.SecretsManager` and exposed as
`MeterDescriptors.MeterName` so consumers can call `AddMeter(...)` without a
magic string. The meter is versioned identically to the activity source — the
version is derived from the assembly informational version, so both track the
package version automatically (fulfilling ADR-0010's "kept in step with the
package version" consequence without manual work).

Instruments are created eagerly at type initialisation, never per call, and are
all tagged with `aws.secretsmanager.secret.id` (the configured secret
identifier — name or ARN — following the OTel attribute naming of the
`aws.secretsmanager.*` semantic-convention family):

| Instrument | Type | Meaning |
| --- | --- | --- |
| `w4k.secretsmanager.loads` | Counter | initial loads attempted |
| `w4k.secretsmanager.reloads` | Counter | reloads that changed configuration data |
| `w4k.secretsmanager.reloads.skipped` | Counter | reloads where the secret version was unchanged |
| `w4k.secretsmanager.failures` | Counter | load or reload failures, additionally tagged with `phase` (`load` / `reload`) |

No duration histogram is emitted. Fetches are rare (one load plus
reload-triggered fetches), so a latency distribution carries almost no
signal; the network round trip is inside AWS and not actionable by the
consumer; and fetch timeouts already surface on the failures counter.
Individual fetch durations remain visible on the `Load`/`Reload` activities
when tracing is enabled.

No instrument carries anything derived from the secret value.

## Consequences

- "Secret has not refreshed" and "reloads are failing" alerts are expressible
  in standard OTel tooling with one `AddMeter` call.
- Instruments no-op when nobody is listening, so this passes guiding principle
  2 ("what the consumer does not opt into, does not run") despite adding
  always-on code to the load path: the cost is a handful of struct tag pairs
  per load/reload, negligible next to the network round trip.
- Reloads dropped by the concurrent-reload guard are not counted anywhere —
  they perform no fetch and change no data.
