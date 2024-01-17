# W4k.Extensions.Configuration.Aws.SecretsManager

![W4k.Either Build](https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager/workflows/Build%20and%20test/badge.svg)
[![NuGet Badge](https://buildstats.info/nuget/W4k.Extensions.Configuration.Aws.SecretsManager?includePreReleases=true)](https://www.nuget.org/packages/W4k.Extensions.Configuration.Aws.SecretsManager/)
[![CodeQL](https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager/security/code-scanning)

Configuration provider for AWS SecretsManager.

## Installation

```shell
dotnet add package W4k.Extensions.Configuration.Aws.SecretsManager
```

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

// add AWS SecretsManager provider for specific secret
builder.Configuration.AddSecretsManager("my-secret-secrets", c => c.ConfigurationKeyPrefix = "AppSecrets");

// add options, bind using `ConfigurationKeyPrefix`
builder.Services
    .AddOptions<Secrets>()
    .BindConfiguration("AppSecrets");
```

Additionally, you can pass `SecretsManagerClient` to the provider:

```csharp
// passing custom SecretsManagerClient
var client = new AmazonSecretsManagerClient(/* ... */);
builder.Configuration.AddSecretsManager("my-secret-secrets", client, c => c.ConfigurationKeyPrefix = "AppSecrets");
```

### Configuration

It is possible to specify other options:

#### Secret Version

If omitted, latest version of the secret will be used, however it is possible to specify custom version or stage:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c =>
    {
        c.Version = new SecretVersion { VersionId = "d6d1b757d46d449d1835a10869dfb9d1" };
    });
```

#### Configuration key prefix

By default, all secret values will be added to the configuration root, however it is possible to specify custom prefix:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => 
    {
        c.ConfigurationKeyPrefix = "Clients:MyService";
    });
```

With example above, secret property of name `Password` will be transformed to `Clients:MyService:Password`.
When binding your option type, make sure path is considered or that you bind to the correct configuration section. 

#### Secret processing (parsing and tokenizing)

By default AWS SecretsManager stores secret as simple key-value JSON object - and thus JSON processor is set as default.
In some cases, user may want to specify custom format, either more complex JSON object, or even XML document.

In order to support such scenarios, it is possible to specify custom secret processor:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => 
    {
        c.Processor = new MyCustomSecretProcessor(); // implements `ISecretsProcessor`
    });
```

There's helper class [`SecretsProcessor<T>`](W4k.Extensions.Configuration.Aws.SecretsManager/SecretsProcessor.cs) which
can be used to simplify implementation of custom processor (by providing implementation of `ISecretStringParser<T>` and `IConfigurationTokenizer<T>`).

#### Configuration key transformation

It is possible to hook configuration key transformation, which is used to transform tokenized configuration key.
By default only [`KeyDelimiterTransformer`](W4k.Extensions.Configuration.Aws.SecretsManager/ConfigurationKeyTransformer.cs) is used.

`KeyDelimiterTransformer` transforms `__` to proper configuration key delimiter, `:`.

To add custom transformation, use property `KeyTransformers`:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => 
    {
        c.KeyDelimiterTransformer.Add(new MyCustomKeyTransformer()); // implements `IConfigurationKeyTransformer`
    });
```

It is also possible to clear even default transformer by simply calling `Clear()` method.

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => 
    {
        c.KeyDelimiterTransformer.Clear();
    });
```

## Acknowledgements

This library is inspired by `Kralizek.Extensions.Configuration.AWSSecretsManager`.

## Alternative approaches

When using AWS Fargate (ECS), you can configure Task Definition to use SecretsManager as a source of environment variables.
This approach is described in [Passing sensitive data to a container / Using Secrets Manager](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/secrets-envvar-secrets-manager.html).

## Alternative packages

- [`Kralizek.Extensions.Configuration.AWSSecretsManager`](https://www.nuget.org/packages/Kralizek.Extensions.Configuration.AWSSecretsManager)
- [`PrincipleStudios.Extensions.Configuration.SecretsManager`](https://www.nuget.org/packages/PrincipleStudios.Extensions.Configuration.SecretsManager)

---

[Setting icons](https://www.flaticon.com/free-icons/setting) created by [Freepik](https://www.flaticon.com/authors/freepik) - [Flaticon](https://www.flaticon.com/)
