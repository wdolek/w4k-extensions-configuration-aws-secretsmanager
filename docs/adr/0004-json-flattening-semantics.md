# 4. JSON flattening semantics

- Status: Accepted
- Date: 2024-01

## Context

The default processor turns a JSON secret into configuration keys. .NET already
defines what that should look like: `Microsoft.Extensions.Configuration.Json`
flattens `appsettings.json` with `:` as the path delimiter and array indices as
segments. Consumers expect a secret to bind exactly like the equivalent
`appsettings.json` fragment.

Deviating from those rules would produce surprising binding failures that are
hard to diagnose, especially since secret values cannot be logged.

## Decision

Mirror the behaviour of `JsonConfigurationFileParser` from `dotnet/runtime`
(the implementation notes this provenance explicitly) using
`System.Text.Json`:

- Objects recurse; key is `prefix:propertyName`.
- Arrays recurse; key is `prefix:index` (invariant formatting).
- `Number` uses `GetRawText()` — preserves the literal exactly as written.
- `String` uses `GetString()`.
- `True`/`False` map to `"True"`/`"False"` (matching `bool.ToString()`, which is
  what the configuration binder expects).
- `Null`/`Undefined` map to a `null` value under the current key.
- Anything else throws `FormatException`.

Additional choices specific to this library:

- **No leading delimiter.** With an empty `ConfigurationKeyPrefix`, the key is
  the property name; `ComposeKey` only inserts `ConfigurationPath.KeyDelimiter`
  when the prefix is non-empty.
- **Nested JSON inside a string value is not re-parsed.** A string stays a
  string. This is deliberate and covered by a unit test; re-parsing would make
  the key shape depend on the value content.
- **Detection before parsing.** `JsonElementParser.IsPossiblyJsonValue` performs
  a cheap bracket check (`{...}` / `[...]`, trimmed length >= 2) before calling
  `JsonDocument.Parse`; a `JsonException` returns `false` rather than
  propagating, so `SecretProcessor<T>` can raise the actionable "have you used
  the appropriate processor?" error.
- **Parser options:** `AllowTrailingCommas = true`,
  `CommentHandling = Skip`, `MaxDepth = 16`. Lenient about hand-edited secrets,
  but bounded against pathological nesting.
- The root `JsonElement` is `Clone()`d so the `JsonDocument` can be disposed
  immediately instead of being kept alive for the process lifetime.

## Consequences

- A secret binds identically to the same JSON placed in `appsettings.json`.
- Only scalar leaves become configuration values; a plain (non-JSON) secret
  string is rejected by the default processor and requires a custom one
  ([ADR-0003](0003-pluggable-secret-processing.md)).
- `MaxDepth = 16` is an arbitrary but explicit limit; deeper secrets fail to
  parse and are reported as a `FormatException`.
- If the upstream JSON configuration provider changes its flattening rules, this
  implementation must be re-aligned.
