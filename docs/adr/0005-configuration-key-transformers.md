# 5. Configuration key transformers

- Status: Accepted
- Date: 2024-01

## Context

Secret keys in AWS are often not written the way configuration paths are. The
Secrets Manager console key/value editor produces flat keys, and the established
convention for expressing hierarchy in flat stores (environment variables, ECS
task definition secrets) is the double underscore: `MyService__Password`.

Since a secret is frequently migrated from, or mirrored by, environment
variables, the same key must resolve to the same configuration path in both
places. Hard-coding one convention would block anyone whose keys use a different
one (e.g. `.` or `-` separators, or a naming scheme imposed by another team).

## Decision

Introduce a one-method abstraction applied to every key produced by the
tokenizer:

```csharp
public interface IConfigurationKeyTransformer
{
    string Transform(string key);
}
```

- `SecretsManagerConfigurationSource.KeyTransformers` is an ordered list; each
  transformer receives the output of the previous one.
- The default list contains exactly one entry, `KeyDelimiterTransformer`, which
  performs `key.Replace("__", ConfigurationPath.KeyDelimiter)`.
- The list can be extended (`AddKeyTransformer`) or emptied
  (`ClearKeyTransformers`) per source.
- Transformation runs inside `SecretProcessor<T>`, after tokenization, so it
  applies uniformly regardless of the secret format.

`ConfigurationKeyPrefix` is intentionally _not_ a transformer: it is passed into
the tokenizer as the root prefix, so it is never subject to key rewriting.

## Consequences

- `MyService__Password` in a secret and `MyService__Password` as an environment
  variable bind to the same `MyService:Password` path.
- Non-default conventions are supported without library changes.
- Transformers run once per key at load/reload only, so the cost is negligible;
  iteration uses `CollectionsMarshal.AsSpan` to avoid enumerator allocation.
- Transformers can collide (two source keys mapping to one path). Last write
  wins, silently. Considered acceptable for a per-source, opt-in feature.
- Custom processors implementing `ISecretProcessor` directly are responsible for
  applying `source.KeyTransformers` themselves.
