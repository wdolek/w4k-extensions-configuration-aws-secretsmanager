## W4k, AWS Secrets Manager configuration provider 

Sample Web API application with OpenTelemetry instrumentation.

Things to notice:

- using AWS Secrets Manager client created using AWS options (see `appsettings.json` for configuration section)
- using key prefix for configuration - fetched secrets need to be bound from same key prefix
- polling watcher checks for changes every minute
- errors occuring during reload phase are ignored (not causing application to crash)
- "Reload" action is traced using OpenTelemetry (shown in console output and Aspire dashboard)

### Scripts (PS)

Create new secret:

```bash
aws secretsmanager create-secret --name 'w4k/awssm/sample-secret' --secret-string '{"ClientId":"eric.mason","ClientSecret":"rosebud"}'
```

Delete secret:

```bash
aws secretsmanager delete-secret --secret-id 'w4k/awssm/sample-secret' --force-delete-without-recovery
```

Run standalone Aspire dashboard:

```bash
podman run --rm -it -d -p 18888:18888 -p 4317:18889 --env "DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true" --name aspire-dashboard mcr.microsoft.com/dotnet/aspire-dashboard:9.0
```

Run sample application:

```bash
dotnet run --project ./SecretApi/SecretApi.csproj
```
