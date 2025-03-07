# W4k.Extensions.Configuration.Aws.SecretsManager

![W4k.Either Build](https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager/workflows/Build%20and%20test/badge.svg)
[![GitHub Release](https://img.shields.io/github/release/wdolek/w4k-extensions-configuration-aws-secretsmanager.svg)](https://github.com/wdolek/w4k-extensions-configuration-aws-secretsmanager/releases)
[![NuGet Version](https://img.shields.io/nuget/v/W4k.Extensions.Configuration.Aws.SecretsManager.svg)](https://www.nuget.org/packages/W4k.Extensions.Configuration.Aws.SecretsManager/)

Configuration provider using AWS Secrets Manager as the source of data.

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
    configurationKeyPrefix: "AppSecrets");

// ... and then bind configuration using `configurationKeyPrefix: "AppSecrets"`
builder.Services
    .AddOptions<Secrets>()
    .BindConfiguration("AppSecrets");
```

Additionally, you can provide instance of `IAmazonSecretsManager`:

```csharp
// passing custom `IAmazonSecretsManager` (e.g. with custom credentials)
var client = new AmazonSecretsManagerClient(/* ... */);
builder.Configuration.AddSecretsManager(
    client,
    "my-secret-secrets",
    configurationKeyPrefix: "AppSecrets");
```

To add more secrets while sharing the same Amazon Secrets Manager client, you can set a default instance first like this:

```csharp
var client = new AmazonSecretsManagerClient(/* ... */);
builder.Configuration.SetSecretsManagerClient(client)
    .AddSecretsManager("my-first-secret")
    .AddSecretsManager("my-second-secret");
```

## Configuration

Configuration is possible using several `AddSecretsManager` overloads. Shortcut methods for adding Secrets Manager
configuration source allows to specify secret name, optionality or configuration key prefix.

Specifying more complex configuration can be done using `AddSecretsManager` method with configure callback.

### Accessing existing configuration

When using secrets manager configuration builder, it's also possible to access existing (already loaded) configuration:

```csharp
// using `ConfigurationManager` provided by application host builder
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    (config, source) => source.WithTimeout(config.GetValue<TimeSpan>("Secrets:FetchTimeout")))
```

assuming your `appsettings.json` contains:

```json5
{
  "Secrets": {
    "FetchTimeout": "00:00:10"
  }
}
```

(of course, you can still just capture `builder.Configuration` in configure action)

### Optional secret

When adding a configuration source, given secret is mandatory by default - meaning if the secret is not found, or it's not possible 
to fetch it, an exception is thrown. To make it optional, set `isOptional` parameter to `true`:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    isOptional: true);
```

It is possible to distinguish between error happening during _load_ and _reload_ (when enabled) operation
by using `OnLoadException` and `OnReloadException` respectively.

```csharp
// ignore exception (do not throw)
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    source => source
        .OnLoadException(ctx => { ctx.Ignore = true; })
        .OnReloadException(ctx => { ctx.Ignore = true; }));
```

Callbacks receive `SecretsManagerExceptionContext` which can be examined to decide whether to ignore the exception or not by flagging its `Ignore` property. 

### Secret Version

If omitted, the latest version of the secret will be used. However, it is possible to specify a custom version or stage:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    source => source.WithVersion(versionId: "d6d1b757d46d449d1835a10869dfb9d1"));
```

### Configuration key prefix

By default, all the secret values will be added to the configuration root. To prevent collisions with other configuration keys,
or to group secret values for further binding, it is possible to specify configuration key prefix as follows:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    configurationKeyPrefix: "Clients:MyService");
```

With example above, secret property of name `Password` will be transformed to `Clients:MyService:Password`.
When binding your option type, make sure path is considered or that you bind to the correct configuration section. 

### Secret processing (parsing and tokenizing)

By default, AWS Secrets Manager stores secret as simple key-value JSON object - and thus JSON processor is set as default.
In some cases, custom format may be used - either a complex JSON object or even an XML document (or actually anything, imagination is the limit).

In order to support such scenarios, it is possible to specify custom secret processor:

```csharp
// implements `ISecretsProcessor`
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    source => source.WithProcessor(new MyCustomSecretProcessor()));
```

There's helper class [`SecretProcessor<T>`](src/W4k.Extensions.Configuration.Aws.SecretsManager/SecretProcessor.cs) which
can be used to simplify implementation of custom processor (by providing implementation of [`ISecretStringParser<T>`](src/W4k.Extensions.Configuration.Aws.SecretsManager/Abstractions/ISecretStringParser.cs) and [`IConfigurationTokenizer<T>`](src/W4k.Extensions.Configuration.Aws.SecretsManager/Abstractions/IConfigurationTokenizer.cs)).

### Configuration key transformation

It is possible to hook into the configuration key transformation, which is used to transform the tokenized configuration key.
By default, only [`KeyDelimiterTransformer`](src/W4k.Extensions.Configuration.Aws.SecretsManager/ConfigurationKeyTransformer.cs) is used.

`KeyDelimiterTransformer` transforms "`__`" to configuration key delimiter, "`:`".

To add custom transformation, use property `KeyTransformers`:

```csharp
// implements `IConfigurationKeyTransformer`
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    source => source.AddKeyTransformer(new MyCustomKeyTransformer()));
```

It is also possible to clear transformers by simply calling `Clear()`, respectively `ClearKeyTransformers()`, method.

```csharp
// assigning values directly to `SecretsManagerConfigurationSource`
source.KeyTransformers.Clear();

// using `SecretsManagerConfigurationBuilder`
source.ClearKeyTransformers();
```

### Refreshing secrets

By default, secrets are not refreshed. In order to enable refreshing, you can set configuration watcher:

```csharp
// implements `IConfigurationWatcher`
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    source => source.WithConfigurationWatcher(new SecretsManagerPollingWatcher(TimeSpan.FromMinutes(5)));
```

```csharp
// uses `SecretsManagerPollingWatcher`
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    source => source.WithPollingWatcher(TimeSpan.FromMinutes(5));
```

When refreshing secrets, use `IOptionsSnapshot<T>` or `IOptionsMonitor<T>` instead of just `IOptions<T>`.
For more details about _Options pattern_, see official documentation [Options pattern in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options).

Please note that there is associated cost of retrieving secret values from AWS Secrets Manager.
Refer to the [AWS Secrets Manager pricing](https://aws.amazon.com/secrets-manager/pricing/) for further information.

> [!IMPORTANT]
> Watcher is started **ONLY** when initial load is successful.

### Preventing hangs

It may happen that there's connection issue with AWS Secrets Manager. In order to prevent unnecessary hangs,
it is possible to configure timeout:

```csharp
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    source => source.WithTimeout(TimeSpan.FromSeconds(42)));
```

Default timeout value can be found at [`SecretsManagerConfigurationSource`](src/W4k.Extensions.Configuration.Aws.SecretsManager/SecretsManagerConfigurationSource.cs).

### Diagnostics

Library uses `ActivitySource` and `Activity` to provide information about _load_ and _refresh_ operations.
To be able to see traces, it is necessary to listen to activity source named "`W4k.Extensions.Configuration.Aws.SecretsManager`".

#### Open Telemetry

Using Open Telemetry package(s), it is possible to add tracing to your application following way:

```csharp
var otel = builder.Services.AddOpenTelemetry();
otel.WithTracing(tracing => tracing
    .AddSource(W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics.ActivityDescriptors.ActivitySourceName)
    .AddConsoleExporter());
```

Since _Load_ happens before host is fully built, you won't see _Load_ activity this way. It is still possible to trace _Refresh_ operation though.

#### Activity listener

With or without Open Telemetry, it is also possible to simply hook activity listener into your application.
There's helper extension method to configure activity listener:

```csharp
var listener = new ActivityListener().ListenToSecretsManagerActivitySource(
    onStart => Console.WriteLine($"[{onStart.StartTimeUtc:O}] {onStart.Source.Name}:{onStart.OperationName} Started"),
    onStop => Console.WriteLine($"[{onStop.StartTimeUtc:O}] {onStop.Source.Name}:{onStop.OperationName} Stopped"));

ActivitySource.AddActivityListener(listener);
```

When listener is registered this way in very early stage of the application, it is possible to see _Load_ activity as well.

### Logging

It is possible to configure logging for the provider:

```csharp
// using Microsoft.Extensions.Logging
builder.Configuration.AddSecretsManager(
    "my-secret-secrets",
    source => source.WithLoggerFactory(LoggerFactory.Create(logging => logging.AddConsole())));
```

By default, logging is disabled (by using `NullLoggerFactory`).

Since logging happens during the host build phase (before the application is fully built), it's not possible to use the final application logger.
Perhaps you will need to configure logging twice - once for the provider and once for the application.

#### Reusing application logger

If your logger requires more complex configuration you don't want to repeat (in configuration phase),
it's possible to pass `ILoggerFactory` instance to the provider retrospectively:

```csharp
public static WebApplication UseAppLoggerInSecretsManagerConfigProvider(this WebApplication app)
{
    var config = app.Services.GetRequiredService<IConfiguration>();
    if (config is IConfigurationRoot root)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        foreach (var configProvider in root.Providers.OfType<SecretsManagerConfigurationProvider>())
        {
            configProvider.Source.LoggerFactory = loggerFactory;
        }
    }

    return app;
}
```

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
