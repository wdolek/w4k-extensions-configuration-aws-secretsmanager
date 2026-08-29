# API surface

Public API shape, usability and deprecations. Read
[ADR-0011](../adr/0011-target-frameworks-and-api-compatibility.md) before
touching anything public.

---

## A1. Add `SecretName` to `SecretRetrievalException`

**Status:** open · **Breaking:** no (additive) · **Effort:** XS

### Why

Every failure is enveloped as `SecretRetrievalException("Failed to fetch secret", inner)`.
With several sources registered, that message does not say *which* secret
failed, and `HandleException` is `[StackTraceHidden]`, so the stack does not
help either. The name is not sensitive (the README already logs it), so there is
no reason to withhold it.

### Where

- `src/.../Abstractions/SecretRetrievalException.cs`
- `src/.../SecretsManagerConfigurationProvider.cs:167`

### Change

Add a property and a constructor overload, keeping the three existing
constructors intact for binary compatibility:

```csharp
public string? SecretName { get; }

public SecretRetrievalException(string message, string? secretName, Exception innerException)
    : base(message, innerException)
{
    SecretName = secretName;
}
```

Then at the throw site:

```csharp
var envelopeException = new SecretRetrievalException(
    $"Failed to fetch secret '{Source.SecretName}'",
    Source.SecretName,
    exception);
```

Also update the `SecretRetrievalException` thrown in `SecretFetcher.cs:35` to
pass the secret id.

### Tests

`tests/.../SecretsManagerConfigurationProviderShould.cs` — assert the thrown
exception carries `SecretName` and that the message contains it.

---

## A2. Deprecate the `bool isOptional` overloads

**Status:** open · **Breaking:** no (obsoletion only) · **Effort:** S

### Why

`ConfigurationBuilderExtensions` exposes ten `AddSecretsManager` overloads. The
simple ones earn their keep; the four taking a positional `bool isOptional` do
not — `AddSecretsManager(client, "name", "prefix", true)` is unreadable at the
call site, and the same thing is expressible and clearer through the
`configureSource` callback.

Adding `[Obsolete]` is a compatible change as far as package validation is
concerned, so no suppression file is needed.

### Where

`src/.../ConfigurationBuilderExtensions.cs`

### Change

**Keep** (no change):

- `AddSecretsManager(IConfigurationBuilder, Action<SecretsManagerConfigurationSource>)`
- `AddSecretsManager(IConfigurationBuilder, string, Action<SecretsManagerConfigurationBuilder>)`
- `AddSecretsManager(IConfigurationManager, string, Action<IConfiguration, SecretsManagerConfigurationBuilder>)`
- `AddSecretsManager(IConfigurationBuilder, string)` — line 133
- `AddSecretsManager(IConfigurationBuilder, string, string)` — line 149
- `AddSecretsManager(IConfigurationBuilder, IAmazonSecretsManager, string)` — line 211
- `AddSecretsManager(IConfigurationBuilder, IAmazonSecretsManager, string, string)` — line 232

**Obsolete** the four `bool isOptional` overloads (lines 170, 187, 254, 277):

```csharp
[Obsolete(
    "Use AddSecretsManager(secretName, source => source.OnLoadException(ctx => ctx.Ignore = true)) instead. " +
    "See https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager#optional-secret",
    DiagnosticId = "W4KSM0001",
    UrlFormat = "https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager#{0}")]
```

Use a `DiagnosticId` so consumers can suppress this specific warning instead of
disabling `CS0618` wholesale.

Keep them obsolete for the whole of 2.x; remove in 4.0 at the earliest (3.0 if
you are comfortable — but obsoleting and removing in consecutive majors is
aggressive for a config provider).

### Tests

Existing tests calling the obsolete overloads will emit warnings. Suppress in
the test project rather than deleting the tests — the overloads still ship and
still need coverage:

```xml
<NoWarn>$(NoWarn);W4KSM0001</NoWarn>
```

### Follow-up

Update README examples to stop showing `isOptional:` — see D1.

---

## A3. Narrow what "optional" means

**Status:** open · **Breaking:** behavioural · **Tag:** `v3` · **Effort:** M
**Depends on:** D1 should ship first (documents current behaviour)

### Why

`isOptional: true` registers a handler that sets `Ignore = true` for *any*
exception. That means a malformed JSON payload (`FormatException`), a throttling
error (`AmazonServiceException`), and an IAM misconfiguration
(`AccessDeniedException`) are all treated identically to "the secret does not
exist". "Optional" should mean *may be absent*, not *may be broken*.

Counter-argument, per
[ADR-0006](../adr/0006-error-handling-and-optional-secrets.md): severity is
deployment-dependent and the flag is documented sugar for "ignore everything",
so it does what it advertises. This is why the task is split — D1 (documentation)
is unambiguously worth doing, this one is a judgement call.

### Risk

This is **behaviourally breaking but not API-breaking**. Signatures do not
change, nothing fails to compile, and `EnablePackageValidation` will not flag
it. An app that today boots with empty config from a malformed secret will,
after this change, fail to start. Silent-to-loud, at startup, in production.
It must be a headline item in the 3.0 release notes.

### Where

- `src/.../ConfigurationBuilderExtensions.cs:15`
  (`OptionalSecretExceptionHandler`)
- `src/.../SecretsManagerConfigurationSource.cs` (builder)

### Change

1. Narrow the built-in handler to absence only:

   ```csharp
   private static readonly Action<SecretsManagerExceptionContext> OptionalSecretExceptionHandler =
       context => context.Ignore = context.Exception is ResourceNotFoundException;
   ```

   Note the callback receives the *raw* exception (enveloping happens after), so
   no `InnerException` unwrapping is needed here.

2. Add a discoverable escape hatch on `SecretsManagerConfigurationBuilder` for
   everything else:

   ```csharp
   public SecretsManagerConfigurationBuilder IgnoreLoadException<TException>()
       where TException : Exception;

   public SecretsManagerConfigurationBuilder IgnoreReloadException<TException>()
       where TException : Exception;
   ```

   Implement by composing onto the existing `OnLoadException` /
   `OnReloadException` delegates rather than replacing them, so multiple calls
   accumulate.

3. Decide explicitly whether `AccessDeniedException` counts as "absent". It
   should **not** — a permissions bug must not be silently tolerated.

### Tests

`tests/.../SecretsManagerConfigurationProviderShould.cs`:

- optional secret + `ResourceNotFoundException` → no throw, empty data
- optional secret + `FormatException` → throws `SecretRetrievalException`
- optional secret + `AmazonServiceException` → throws
- `IgnoreLoadException<FormatException>()` → does not throw

---

## A4. Decouple `ISecretProcessor` from the configuration source

**Status:** open · **Breaking:** yes · **Tag:** `v3` · **Effort:** M

### Why

`ISecretProcessor.GetConfigurationData(SecretsManagerConfigurationSource source, string secretString)`
hands a custom processor the AWS client, the timeout, the exception callbacks
and the logger factory — none of which it needs. Worse, per
[ADR-0005](../adr/0005-configuration-key-transformers.md), a custom processor is
*silently responsible* for applying `KeyTransformers` and honouring
`ConfigurationKeyPrefix`. Nothing in the signature communicates that, and
nothing enforces it, so every third-party processor gets it wrong at least once
and the symptom is "my `__` keys stopped working".

### Change

Move prefix and transformer application out of the processor and into the
provider, so the semantics are guaranteed regardless of processor:

1. Narrow the contract:

   ```csharp
   public interface ISecretProcessor
   {
       IEnumerable<KeyValuePair<string, string?>> GetConfigurationData(
           SecretProcessingContext context,
           string secretString);
   }

   public readonly struct SecretProcessingContext
   {
       public string SecretName { get; }   // only needed for error messages
   }
   ```

2. `SecretProcessor<T>` keeps composing `ISecretStringParser<T>` +
   `IConfigurationTokenizer<T>`, but no longer touches transformers.

3. The provider builds the dictionary: tokenize → apply
   `ConfigurationKeyPrefix` → apply `KeyTransformers` in order → write into an
   `OrdinalIgnoreCase` dictionary. Keep the existing last-write-wins collision
   behaviour from ADR-0005.

4. Add an ADR superseding the relevant part of ADR-0005, recording that
   transformer application moved from the processor to the provider.

### Risk

Breaks any consumer with a custom `ISecretProcessor`. Likely a very small
population, and the migration is mechanical (delete the transformer loop). The
built-in JSON path is unaffected from the outside.

### Tests

- `tests/.../SecretProcessorShould.cs` — rework for the new contract.
- Add a test with a custom processor proving transformers are applied *without*
  the processor doing anything.

---

## A5. Snapshot `KeyTransformers` when building the provider

**Status:** open · **Breaking:** no · **Effort:** XS

### Why

`SecretsManagerConfigurationSource.KeyTransformers` is a live `List<>` shared
with the provider and read via `CollectionsMarshal.AsSpan` during processing. A
consumer mutating it after host build changes reload behaviour mid-flight, and
mutating it concurrently with a watcher-driven reload is a genuine race against
the span.

The README currently advertises `KeyTransformers.Clear()` as a supported way to
configure, so the list must stay publicly mutable *before* build.

### Where

- `src/.../SecretsManagerConfigurationSource.cs:48`
- `src/.../SecretProcessor.cs`

### Change

In `Build()`, copy the transformers into an array and pass that to the provider
(or store it on the provider). Processing reads the immutable array. Mutating
the source list after `Build()` then simply has no effect, which is the
principle-of-least-surprise outcome.

If A4 is done first, the snapshot naturally lives on the provider and this task
folds into it.

### Tests

`tests/.../KeyDelimiterTransformerShould.cs` or provider tests — assert that
adding a transformer after `Build()` does not affect a subsequent reload.
