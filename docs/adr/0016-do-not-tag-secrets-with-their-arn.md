# 16. Do not tag secrets with their ARN

- Status: Accepted
- Date: 2026-09

## Context

[ADR-0010](0010-diagnostics-and-late-bound-logging.md) states that log and
trace payloads carry secret **name** and **version id** only, never the value.
An earlier revision of the activity-tagging feature also tagged the secret's
full ARN, as returned by `GetSecretValueAsync`:

```csharp
activity?.SetTag("aws.secretsmanager.secret.arn", secret.Arn);
```

An AWS Secrets Manager ARN has the shape
`arn:aws:secretsmanager:{region}:{account-id}:secret:{name}-{suffix}` — it
embeds the caller's AWS **account id**. Traces routinely leave the process
boundary (OTel exporters, SaaS observability backends, log aggregators run by
a third party), so tagging the ARN pushes the account id into every one of
those systems, for every load and reload. That directly contradicts the
"name and version id only" rule ADR-0010 already committed to; the ARN tag
should never have shipped.

The identifier the consumer configured (`Source.SecretName`, already tagged
as `aws.secretsmanager.secret.id`) is sufficient to correlate telemetry with a
secret. If a consumer configured a full ARN as the secret name in the first
place, that is their own choice, made with knowledge of their own account id -
not something this library fetches and re-exposes on their behalf.

## Decision

`SecretFetcher` does not read or retain `GetSecretValueResponse.ARN`, and
`SecretsManagerConfigurationProvider` does not tag activities (or any other
telemetry) with the secret's ARN. Only `aws.secretsmanager.secret.id` (as
configured) and `aws.secretsmanager.secret.version_id` (as fetched) are
emitted.

This is a permanent constraint, not an oversight: do not reintroduce an ARN
tag, on activities, metrics, or logs, even though the ARN is available on the
fetch response and adding it back is a one-line change.

## Consequences

- Telemetry emitted by this library never carries the consumer's AWS account
  id, regardless of exporter or backend configuration.
- A consumer who genuinely wants the ARN correlated with their traces can
  configure `SecretName` as the full ARN themselves ([ADR-0009](0009-secrets-manager-client-resolution.md))
  - the tagged `secret.id` then already is the ARN, by the consumer's own
    choice.
- `SecretFetcher`'s internal `SecretValue` carries only `VersionId` and
  `Value`; there is no `Arn` property to accidentally wire up elsewhere later.

## Related

- [ADR-0010](0010-diagnostics-and-late-bound-logging.md) - establishes the
  "name and version id only" rule this decision enforces for the ARN
  specifically.
- [ADR-0009](0009-secrets-manager-client-resolution.md) - a consumer may
  configure `SecretName` as a full ARN; that is a separate, consumer-owned
  decision unaffected by this ADR.
