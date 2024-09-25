# W4k.Extensions.Configuration.Aws.SecretsManager

![W4k.Either Build](https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager/workflows/Build%20and%20test/badge.svg)
[![GitHub Release](https://img.shields.io/github/release/wdolek/w4k-extensions-configuration-aws-secretsmanager.svg)](https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager/releases)
[![NuGet Version](https://img.shields.io/nuget/v/W4k.Extensions.Configuration.Aws.SecretsManager.svg)](https://www.nuget.org/packages/W4k.Extensions.Configuration.Aws.SecretsManager/)
[![CodeQL](https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager/security/code-scanning)

Configuration provider using AWS Secrets Manager as source of data.

Using this provider, you can load secrets from AWS Secrets Manager and bind them to your configuration classes, using
all features of Options pattern (`IOptions<T>`).

The provider supports **refreshing secrets** (by polling, it's possible to provide your own mechanism)
and **custom secret processing** (which allows parsing formats other than JSON when using binary secrets).

## Installation

```shell
dotnet add package W4k.Extensions.Configuration.Aws.SecretsManager
```

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

// add AWS Secrets Manager Configuration Provider for specific secret
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => c.ConfigurationKeyPrefix = "AppSecrets");

// ... and then bind configuration using `ConfigurationKeyPrefix` = "AppSecrets"
builder.Services
    .AddOptions<Secrets>()
    .BindConfiguration("AppSecrets");
```

Additionally, you can pass `IAmazonSecretsManager` to the provider:

```csharp
// passing custom `IAmazonSecretsManager` (e.g. with custom credentials)
var client = new AmazonSecretsManagerClient(/* ... */);
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    client,
    c => c.ConfigurationKeyPrefix = "AppSecrets");
```

In order to add multiple secrets while sharing same configuration,
it's possible to use another overload of `AddSecretsManager` method:

```csharp
// add AWS Secrets Manager Configuration Provider for multiple secrets
builder.Configuration.AddSecretsManager(["my-first-secret", "my-second-secret"]);
```

## Configuration

### Optional secret

When adding a configuration source, given secret is mandatory by default - meaning if the secret is not found, or it's not possible 
to fetch it, an exception is thrown. To make it optional, set `IsOptional` property to `true`:

```csharp
builder.Configuration.AddSecretsManager("my-secret-secrets", c => c.IsOptional = true);
```

### Secret Version

If omitted, latest version of the secret will be used, however it is possible to specify custom version or stage:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c =>
    {
        c.Version = new SecretVersion { VersionId = "d6d1b757d46d449d1835a10869dfb9d1" };
    });
```

### Configuration key prefix

By default, all the secret values will be added to the configuration root. To prevent collisions with other configuration keys,
or to group secret values for further binding, it is possible to specify configuration key prefix as follows:

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

### Secret processing (parsing and tokenizing)

By default, AWS Secrets Manager stores secret as simple key-value JSON object - and thus JSON processor is set as default.
In some cases, a user may want to specify a custom format, either a complex JSON object or even an XML document.

In order to support such scenarios, it is possible to specify custom secret processor:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => 
    {
        // implements `ISecretsProcessor`
        c.Processor = new MyCustomSecretProcessor();
    });
```

There's helper class [`SecretProcessor<T>`](src/W4k.Extensions.Configuration.Aws.SecretsManager/SecretProcessor.cs) which
can be used to simplify implementation of custom processor (by providing implementation of [`ISecretStringParser<T>`](src/W4k.Extensions.Configuration.Aws.SecretsManager/Abstractions/ISecretStringParser.cs) and [`IConfigurationTokenizer<T>`](src/W4k.Extensions.Configuration.Aws.SecretsManager/Abstractions/IConfigurationTokenizer.cs)).

### Configuration key transformation

It is possible to hook into the configuration key transformation, which is used to transform the tokenized configuration key.
By default, only [`KeyDelimiterTransformer`](src/W4k.Extensions.Configuration.Aws.SecretsManager/ConfigurationKeyTransformer.cs) is used.

`KeyDelimiterTransformer` transforms "`__`" to configuration key delimiter, "`:`".

To add custom transformation, use property `KeyTransformers`:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => 
    {
        // implements `IConfigurationKeyTransformer`
        c.KeyDelimiterTransformer.Add(new MyCustomKeyTransformer());
    });
```

It is also possible to clear transformers by simply calling `Clear()` method.

```csharp
c.KeyDelimiterTransformer.Clear();
```

### Refreshing secrets

By default, secrets are not refreshed. In order to enable refreshing, you can configure `ConfigurationWatcher` property:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => 
    {
        // implements `IConfigurationWatcher`
        c.ConfigurationWatcher = new SecretsManagerPollingWatcher(TimeSpan.FromMinutes(5));
    });
```

The watcher won't be started if the initial secret load fails.

When refreshing secrets, use `IOptionsSnapshot<T>` or `IOptionsMonitor<T>` instead of just `IOptions<T>`.
For more details about _Options pattern_, see official documentation [Options pattern in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options).

Please note that there is associated cost of retrieving secret values from AWS Secrets Manager.
Refer to the [AWS Secrets Manager pricing](https://aws.amazon.com/secrets-manager/pricing/) for further information.

### Startup behavior

It may happen that there's connection issue with AWS Secrets Manager. In order to prevent unnecessary hangs, it is possible to configure startup timeout:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => 
    {
        c.Startup.Timeout = TimeSpan.FromSeconds(42);
    });
```

If the secret is not loaded within the specified timeout **AND** the source is not optional, an exception will be thrown.

### Logging

It is possible to configure logging for the provider:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    c => 
    {
        // using Microsoft.Extensions.Logging
        c.LoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
    });
```

By default logging is disabled (by using `NullLoggerFactory`).

Since logging happens during the host build phase (before the application is fully built), it's not possible to use the final application logger.
Perhaps you will need to configure logging twice - once for the provider and once for the application.

## Acknowledgements

This library is inspired by `Kralizek.Extensions.Configuration.AWSSecretsManager`.

## Alternative approaches

When using AWS Fargate (ECS), you can configure Task Definition to use Secrets Manager as a source of environment variables.
This approach is described in [Passing sensitive data to a container / Using Secrets Manager](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/secrets-envvar-secrets-manager.html).

## Alternative packages

- [`Kralizek.Extensions.Configuration.AWSSecretsManager`](https://www.nuget.org/packages/Kralizek.Extensions.Configuration.AWSSecretsManager)
- [`PrincipleStudios.Extensions.Configuration.SecretsManager`](https://www.nuget.org/packages/PrincipleStudios.Extensions.Configuration.SecretsManager)
- [`Tiger.Secrets`](https://www.nuget.org/packages/Tiger.Secrets)

---

[Setting icons](https://www.flaticon.com/free-icons/setting) created by [Freepik](https://www.flaticon.com/authors/freepik) - [Flaticon](https://www.flaticon.com/)
