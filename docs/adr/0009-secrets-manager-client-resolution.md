# 9. Secrets Manager client resolution

- Status: Accepted
- Date: 2024-01

## Context

Configuration sources are built before the DI container exists
([ADR-0001](0001-configuration-source-with-synchronous-load.md)), so the library
cannot resolve `IAmazonSecretsManager` from services. It must obtain a client
some other way.

An `AmazonSecretsManagerClient` owns an HTTP handler and a credentials resolution
chain; creating one per source is wasteful, and applications that load several
secrets should not pay that cost repeatedly. At the same time, many applications
have a single, already-configured client (from `GetAWSOptions()`, a custom
credentials provider, a LocalStack endpoint, or a test double) that must be used
instead of a default one.

Taking a dependency on `AWSSDK.Extensions.NETCore.Setup` to read AWS options was
rejected ([ADR-0002](0002-minimal-dependency-footprint.md)).

## Decision

Resolve the client in `SecretsManagerConfigurationSource.Build` using a three-step
fallback:

```csharp
SecretsManager ??= builder.GetSecretsManagerClient() ?? new AmazonSecretsManagerClient();
```

1. **Explicit per-source client** — `source.SecretsManager` / `WithSecretsManager(...)`
   / the client-taking `AddSecretsManager` overloads.
2. **Builder-shared client** — stored in `IConfigurationBuilder.Properties` under
   the private key `"W4k:SecretsManagerClient"`, set via
   `SetSecretsManagerClient(client)` and read via `GetSecretsManagerClient()`.
   `IConfigurationBuilder.Properties` is the framework's own mechanism for
   sharing state between sources, so no new concept is introduced.
3. **Default client** — `new AmazonSecretsManagerClient()`, which uses the
   standard AWS credential and region resolution chain.

Resolution is deferred to `Build()`, not to the `AddSecretsManager` call, so a
client set later in the chain still applies to sources added earlier.

The library never disposes the client: it does not own clients passed in, and the
default one must stay alive as long as the provider might reload.

## Consequences

- `SetSecretsManagerClient(...)` followed by several `AddSecretsManager(...)`
  calls shares one client and one credentials chain.
- Zero-configuration usage works out of the box in environments where the default
  AWS chain is sufficient (ECS/EKS task roles, EC2 instance profile, env vars).
- Custom endpoints, profiles and mocked clients are supported without the library
  knowing about AWS configuration formats.
- Integration tests inject a real profile-based client; unit tests inject a mock.
- A default-constructed client is leaked (never disposed) if no explicit client is
  provided. Accepted — its lifetime matches the application's.
- Secret name resolution stays deliberately dumb: `SecretName` is passed verbatim
  as `SecretId`, so both a plain name and a full ARN work, and no naming
  convention is imposed.
