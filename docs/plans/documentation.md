# Documentation

Cheap, low-risk wins. No code changes.

---

## D1. Document what `isOptional` actually swallows

**Status:** open · **Effort:** XS · **Priority:** high
**Related:** [A3](api-surface.md#a3-narrow-what-optional-means)

### Why

`isOptional: true` reads as "the secret may not exist". It actually registers a
handler that sets `Ignore = true` for **every** exception — a malformed JSON
payload, a throttling error and an IAM denial are all treated as "absent", and
the app boots with that secret's configuration silently missing.

That is a deliberate decision
([ADR-0006](../adr/0006-error-handling-and-optional-secrets.md): severity is
deployment-dependent, the flag is sugar for "ignore everything"), so the fix
here is honesty in the docs, not a behaviour change. Changing the behaviour is
tracked separately as A3 and is v3 material.

### Where

`README.md`, section "Optional secret".

### Change

Add an explicit warning after the existing `isOptional` example:

> **Note:** `isOptional: true` ignores *all* exceptions, not just "secret not
> found". A malformed secret payload, a throttled request or a missing IAM
> permission will also be ignored, and the application will start with the
> secret's configuration absent — which typically surfaces later as an options
> validation failure, or as a `null` at first use.
>
> For finer control, handle the exception yourself. The callback receives the
> original exception (enveloping into `SecretRetrievalException` happens
> afterwards), so no `InnerException` unwrapping is needed:
>
> ```csharp
> builder.Configuration.AddSecretsManager(
>     "my-secret",
>     source => source.OnLoadException(ctx =>
>         ctx.Ignore = ctx.Exception is ResourceNotFoundException));
> ```

Make sure the snippet is checked against the actual `SecretsManagerExceptionContext`
API before committing.

---

## D2. Document binary secret support honestly

**Status:** open · **Effort:** XS
**Depends on:** [C1](correctness.md#c1-binary-secrets-are-base64-decoded-twice)

### Why

Binary secrets are handled in `SecretFetcher` but not mentioned in the README,
and until C1 lands the path is broken for any payload that is not itself
base64-encoded.

### Change

After C1 is fixed, add a short note to the README stating that secrets stored as
`SecretBinary` are decoded as UTF-8 and then handed to the configured processor,
exactly like `SecretString`. If C1 is *not* going to be fixed promptly, instead
document the current limitation.

---

## D3. Document the custom `ISecretProcessor` contract

**Status:** open · **Effort:** XS
**Superseded by:** [A4](api-surface.md#a4-decouple-isecretprocessor-from-the-configuration-source)
if that ships

### Why

Per [ADR-0005](../adr/0005-configuration-key-transformers.md), a custom
`ISecretProcessor` is responsible for applying `KeyTransformers` and
`ConfigurationKeyPrefix` itself. The README's "Secret processing" section shows
`WithProcessor(new MyCustomSecretProcessor())` without mentioning this, so the
predictable outcome is a custom processor where `__` keys silently stop being
translated.

### Change

Add to the README "Secret processing" section:

> When implementing `ISecretProcessor` directly you take over the whole
> pipeline: you must apply `source.ConfigurationKeyPrefix` and every transformer
> in `source.KeyTransformers` yourself, and produce keys in an
> `OrdinalIgnoreCase` dictionary. If you only need a different *format*,
> implement `ISecretStringParser<T>` and `IConfigurationTokenizer<T>` and
> compose them with `SecretProcessor<T>` instead — that handles prefixing and
> transformation for you.

Delete this task if A4 lands, since the responsibility moves to the provider.

---

## D4. Note the `ActivitySource` version in the diagnostics section

**Status:** open · **Effort:** XS
**Depends on:** [C5](correctness.md#c5-activitysource-version-string-has-drifted-from-the-package-version)

### Why

Minor, but the README's "Diagnostics" section names the activity source and its
activities without mentioning that the source carries a version, which some
OTel processors surface as `otel.library.version`.

### Change

One sentence after C5 makes the version track the package version
automatically.
