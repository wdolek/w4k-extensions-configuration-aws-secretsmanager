# 14. Binary secrets must be valid UTF-8

- Status: Accepted
- Date: 2026-09

## Context

[ADR-0003](0003-pluggable-secret-processing.md) normalises binary secrets in
`SecretFetcher` before processing: the payload is decoded and interpreted as a
UTF-8 string, so processors only ever deal with `string`.

Two defects hid in that step:

1. The AWS SDK has *already* base64-decoded `SecretBinary` from the wire
   (`MemoryStreamUnmarshaller`); decoding again in the fetcher threw
   `FormatException` for any real binary secret whose content is not itself
   base64.
2. With the double decode removed, the raw bytes were decoded with
   `Encoding.UTF8`, which **replaces** invalid byte sequences with U+FFFD
   instead of throwing. A certificate, signing key or p12 stored as
   `SecretBinary` therefore round-tripped into silently mangled configuration
   values — the worst failure mode, because nothing ever complains.

Only secrets written via `SecretBinary` reach this branch (console and
`--secret-string` secrets populate `SecretString`), and `SecretsProcessor.Json`
happens to reject mangled bytes later. With a plain-text processor
([ADR-0015](0015-built-in-plain-text-secret-processor.md)) the corrupted value
would flow straight into configuration, making the defect user-visible.

## Decision

`SecretFetcher` reads `SecretBinary` as the raw plaintext bytes (no base64
decoding) and decodes them with a strict
`UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)`.
A non-UTF-8 payload throws `DecoderFallbackException` at fetch time, enveloped
into `SecretRetrievalException` by the existing error handling
([ADR-0006](0006-error-handling-and-optional-secrets.md)).

Failing loudly beats corrupting silently. This refines the binary-normalisation
step described in ADR-0003 — the processing pipeline itself is unchanged.

An alternative was rejected: base64-encoding the payload in the fetcher so a
processor could decode it — that changes what *every* processor sees and breaks
JSON-on-binary-secret behaviour.

## Consequences

- Secrets written via `SecretBinary` with non-UTF-8 content fail at load
  instead of producing U+FFFD-mangled values — a population that was broken
  anyway.
- Binary formats that are not UTF-8 text (certificates, p12) remain
  unsupported: handing processors raw bytes would be a public contract change
  and is deliberately out of scope for 2.x.
- The behaviour change is invisible to package validation (no signature
  change) and must be called out in release notes.
