# Performance

Payloads are capped at 64 KB by AWS and the dominant cost is the network round
trip, so almost nothing here is worth doing for CPU/allocation reasons alone.
Ranked by real-world impact; the honest answer for most of this section is
"don't bother yet".

---

## P1. Reduce AWS call count with `BatchGetSecretValue`

**Status:** open · **Breaking:** no (additive API) · **Effort:** L
**Priority:** low until someone asks

### Why

Secrets Manager bills per API call. An app with N secrets makes N
`GetSecretValue` calls at startup, plus N per poll interval if a watcher is
configured. `BatchGetSecretValue` retrieves up to 20 secrets in one call, cutting
both cost and startup latency.

No new dependency required — it is on the same `IAmazonSecretsManager` client,
so [ADR-0002](../adr/0002-minimal-dependency-footprint.md) is satisfied.

### Reality check

Typical observed usage is 1-2 secrets per app, where this saves one call and
maybe 100 ms of cold start. Nobody has asked for it. Do not start this until
there is a concrete request or a measured startup problem.

### Constraints

- Batch only works for secrets in the same region, and does not support
  per-secret `VersionId` — only `VersionStage`. Sources pinning a `VersionId`
  must fall back to individual `GetSecretValue`.
- Partial failure is normal: the response has an `Errors` collection. Each
  secret's error must be routed to *that source's* `OnLoadException`, not to a
  shared handler.
- Requires `secretsmanager:BatchGetSecretValue` IAM permission, which existing
  deployments will not have. Must be strictly opt-in, and the docs must say so.

### Sketch

One source still equals one secret — this only changes fetch scheduling:

```csharp
builder.Configuration.AddSecretsManager(
    ["db-creds", "api-keys"],
    (secretName, source) => source.WithConfigurationKeyPrefix(secretName));
```

The extension performs one batch fetch, then registers N sources whose `Load()`
resolves from the already-materialised results. Needs an ADR covering the
partial-failure semantics and the IAM requirement.

---

## P2. Parallel prefetch of independent secrets

**Status:** **rejected for now** · **Effort:** M

### Why it was considered

`ConfigurationRoot`'s constructor calls `provider.Load()` sequentially, so N
secrets cost N round trips end to end (~100 ms each) even though the fetches are
completely independent. Starting all `Task<SecretValue>` up front and having
each `Load()` block on its own in-flight task would overlap them without
breaking the synchronous contract in
[ADR-0001](../adr/0001-configuration-source-with-synchronous-load.md).

### Why it is rejected

1. It does not work for the main usage pattern. With
   `WebApplicationBuilder.Configuration` (an `IConfigurationManager`), `Add`
   triggers `ReloadSources()` immediately, so each `AddSecretsManager(...)` call
   loads *at the point of the call*. There is never a window where all sources
   are registered but none are loaded, which is exactly what transparent
   prefetch relies on.
2. Observed usage is 1-2 secrets per app — roughly 100 ms of theoretical saving.
3. It introduces fire-and-forget tasks that may never be awaited (source added
   to a discarded builder), so unobserved-exception handling becomes necessary.

If it is ever revisited, the workable shape is the explicit batch entry point
from **P1**, not a transparent prefetch. Keep this entry so the reasoning is not
re-derived.

---

## P3. Fuse JSON parse and tokenize to avoid `JsonDocument`

**Status:** open · **Effort:** M · **Priority:** very low

### Why

`JsonElementParser` calls `RootElement.Clone()` purely so the element can
outlive the `JsonDocument` — an artefact of splitting
`ISecretStringParser<T>` from `IConfigurationTokenizer<T>`. `Clone()` on a root
element deep-copies the document's backing data. `JsonElementTokenizer` then
builds every key with `$"{prefix}:{name}"`, allocating a new string per level
per node.

A fused processor using `Utf8JsonReader` over the UTF-8 bytes would avoid
`JsonDocument` entirely and could build keys with a pooled `StringBuilder` and a
prefix stack.

### Reality check

At a 64 KB ceiling this saves microseconds and a few KB, **once per process**
(or once per poll interval). It is not measurable against a single HTTPS round
trip. Only do this if you are already rewriting the processing pipeline for
**A4**, and even then only if it does not complicate the public contract.

### Where

- `src/.../Json/JsonElementParser.cs`
- `src/.../Json/JsonElementTokenizer.cs:82-90`

### Constraint

Any rewrite must preserve the exact flattening semantics recorded in
[ADR-0004](../adr/0004-json-flattening-semantics.md) — number raw text, bools as
`"True"`/`"False"`, null values, array indices, no re-parsing of nested JSON
strings. The existing `JsonElementTokenizerShould` tests are the contract; they
must pass unchanged.

### Verification

Add a BenchmarkDotNet project before starting, and do not merge unless it shows
a meaningful win on a representative 8-16 KB secret. If the numbers are in the
noise, close the task.
