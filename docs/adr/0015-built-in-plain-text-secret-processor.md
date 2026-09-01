# 15. Built-in plain text secret processor

- Status: Accepted
- Date: 2026-09

## Context

A secret holding a single opaque value — a password, an API key, a connection
string — is an extremely common Secrets Manager pattern.
[ADR-0003](0003-pluggable-secret-processing.md) shipped JSON as the only
built-in processor, which rejects such payloads ("Secret '...' cannot be
parsed, have you used appropriate secrets processor?"). Users had to write a
custom `ISecretProcessor` for the simplest possible case — and ad-hoc
processors tend to skip prefix composition and key transformation, the trap
called out in [ADR-0005](0005-configuration-key-transformers.md).

The open question was where a single value should land in configuration.

## Decision

Ship `PlainTextSecretProcessor` as a second built-in (`SecretsProcessor.PlainText`,
`WithPlainTextProcessor(...)`), with two key modes:

- **Explicit key** — the value lands under
  `{ConfigurationKeyPrefix}:{configurationKey}`, composed with
  `ConfigurationPath.KeyDelimiter` exactly like object keys in the JSON
  tokenizer ([ADR-0004](0004-json-flattening-semantics.md)). An empty prefix is
  fine; the key then stands alone. A whitespace-only key is rejected with
  `ArgumentException` at construction.
- **No explicit key** — the key is the `ConfigurationKeyPrefix` verbatim. An
  empty prefix is a configuration error — a value cannot live at the
  configuration root — and throws `InvalidOperationException` at processing
  time, with a message telling the user to set a prefix or pass an explicit
  key.

Both modes keep one mental model: *the prefix namespaces every key this secret
produces*, identical to JSON tokenization. The explicit key decouples the
configuration key from the secret name, which is often a path like
`prod/myapp/stripe`, unusable as a config key.

Key transformers apply to the final composed key
([ADR-0005](0005-configuration-key-transformers.md)), and the secret value is
byte-exact: trailing newlines are **not** trimmed, trimming would be a silent
data change.

## Consequences

- ADR-0003's "JSON as the only built-in implementation" is extended, not
  reversed: the processing pipeline is unchanged, and the composition helper
  `SecretProcessor<T>` still applies.
- A non-UTF-8 binary secret would previously flow corrupted through a
  plain-text processor into configuration; with
  [ADR-0014](0014-binary-secrets-must-be-valid-utf8.md) it fails at fetch
  instead.
- The single-key semantics are deliberately minimal; composite formats
  (dotenv, base64-wrapped payloads) can build on the same contract without
  reopening this decision.
