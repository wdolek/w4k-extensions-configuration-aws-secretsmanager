# 17. Binary secret decode failures omit the payload

- Status: Accepted
- Date: 2026-09

## Context

[ADR-0014](0014-binary-secrets-must-be-valid-utf8.md) decided that a
non-UTF-8 `SecretBinary` payload must fail loudly - `SecretFetcher` decodes it
with a strict `UTF8Encoding(throwOnInvalidBytes: true)`, and a non-UTF-8
payload throws `DecoderFallbackException`. ADR-0014 described that exception
as "enveloped into `SecretRetrievalException` by the existing error
handling" - i.e. kept as the `InnerException` of the exception that ultimately
surfaces from `Load()`/`Reload()`.

That envelope leaks the secret. `DecoderFallbackException.Message` embeds the
raw offending bytes in hex, e.g.:

> Unable to translate bytes [C3] at index 2 from specified code page to
> Unicode.

An exception logged via `logger.FailedToLoadSecret(ex, secretName)`, or
recorded on an `Activity` via `AddException(ex)` (net9.0+), captures this
message immediately - before any later wrapping happens. Keeping the original
exception as `InnerException` does not help either: `Exception.ToString()`
recurses into `InnerException` and prints its message too, so any code path
that logs the outer exception "for completeness" reproduces the leak one
level down. For a certificate, signing key, or other binary secret that fails
this check, a handful of its raw bytes would otherwise end up in logs and
traces - a narrower version of exactly the value-leak ADR-0010 forbids.

## Decision

`SecretFetcher` catches `DecoderFallbackException` at the point it is thrown
and replaces it with a `SecretRetrievalException` that names only the secret
id and explains the failure in generic terms - `DecoderFallbackException` is
deliberately **not** chained as `InnerException`.

```csharp
catch (DecoderFallbackException)
{
    throw new SecretRetrievalException(
        $"Secret '{request.SecretId}' is stored as binary and its content is not valid UTF-8; " +
        "binary secrets must decode as UTF-8 text (see ADR-0014)",
        request.SecretId);
}
```

This is a permanent constraint, not an oversight: do not chain the original
`DecoderFallbackException` (or any future exception type whose default
message may embed payload bytes) back onto the exception raised for a decode
failure.

## Consequences

- No part of a binary secret's raw content can reach logs or traces via this
  failure path, no matter how the resulting exception is logged upstream.
- The stack trace/type of the original `DecoderFallbackException` is lost;
  troubleshooting relies on the sanitized message plus the secret id and
  version, which is sufficient - the failure is deterministic (any non-UTF-8
  `SecretBinary` payload) and not something a stack trace would add
  diagnostic value to.
- `SecretRetrievalException.InnerException` being `null` for this specific
  failure is expected and covered by tests; do not "fix" it by re-attaching
  the original exception.

## Related

- [ADR-0014](0014-binary-secrets-must-be-valid-utf8.md) - establishes the
  strict-UTF-8 decode and the `DecoderFallbackException` this decision
  intercepts.
- [ADR-0010](0010-diagnostics-and-late-bound-logging.md) - the "never log the
  value" rule this decision extends to failure paths, not just the happy
  path.
