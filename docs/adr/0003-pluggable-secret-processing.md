# 3. Pluggable secret processing

- Status: Accepted
- Date: 2024-01

## Context

A secret stored in AWS Secrets Manager is an opaque string (`SecretString`) or a
binary blob (`SecretBinary`). The console encourages key/value secrets, which are
stored as flat JSON, but nothing prevents storing YAML, XML, an INI file, a
connection string, a PEM certificate, or a proprietary format.

The configuration system needs a `Dictionary<string, string?>`. Something must
translate the secret payload into that shape, and the library cannot know every
format — nor should it take a dependency on parsers for formats it does not need
([ADR-0002](0002-minimal-dependency-footprint.md)).

## Decision

Split translation into a small pipeline behind interfaces, with JSON as the only
built-in implementation.

```csharp
public interface ISecretProcessor
{
    Dictionary<string, string?> GetConfigurationData(SecretsManagerConfigurationSource source, string secretString);
}
```

For the common "parse, then flatten" shape, provide a reusable composition of two
narrower abstractions:

```csharp
public interface ISecretStringParser<TOut>          // string -> TOut
public interface IConfigurationTokenizer<in T>      // TOut  -> key/value pairs
```

combined by the public `SecretProcessor<T>` helper. The default is
`SecretsProcessor.Json`, i.e.
`new SecretProcessor<JsonElement>(new JsonElementParser(), new JsonElementTokenizer())`,
selected per source via `source.WithProcessor(...)`.

`SecretProcessor<T>` also owns the key-transformation step
([ADR-0005](0005-configuration-key-transformers.md)) and builds the resulting
dictionary with `StringComparer.OrdinalIgnoreCase`, matching the case-insensitive
semantics of `IConfiguration`.

Binary secrets are normalised in `SecretFetcher` before processing: the stream is
read, base64-decoded and interpreted as a UTF-8 string. Processors therefore only
ever deal with `string`, and a custom processor can handle binary formats too.

A payload the parser rejects results in a `FormatException` naming the secret and
hinting at processor selection, rather than a silent empty configuration.

## Consequences

- Non-JSON secrets are supported without the library shipping extra parsers.
- Consumers implementing a custom format usually only write a parser plus a
  tokenizer and reuse `SecretProcessor<T>`; `ISecretProcessor` remains available
  for full control.
- `SecretProcessor<T>` and the two interfaces are public API and therefore
  subject to binary-compatibility checks
  ([ADR-0011](0011-target-frameworks-and-api-compatibility.md)).
- The processor sees the secret as a whole; streaming very large secrets is not
  supported. Acceptable — Secrets Manager caps secret size at 64 KB.
