using System.Collections.Concurrent;
using System.Diagnostics;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretsManagerDiagnosticsShould
{
    private static readonly GetSecretValueResponse InitialSecretValueResponse = new()
    {
        VersionId = "d6d1b757d46d449d1835a10869dfb9d1",
        SecretString = """
            {
                "AppSettingsKey": "Value"
            }
            """
    };

    [Test]
    public async Task TagLoadActivityWithSecretIdAndVersionId()
    {
        // arrange
        var secretName = NewUniqueSecretName();
        using var listener = StartActivityListener(out var activities);

        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(InitialSecretValueResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = secretName, SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        provider.Load();

        // assert
        var activity = FindActivity(activities, ActivityDescriptors.LoadActivityName, secretName);
        await Assert.That(activity).IsNotNull();
        await Assert.That(GetTag(activity!, "aws.secretsmanager.secret_id")).IsEqualTo(secretName);
        await Assert.That(GetTag(activity!, "aws.secretsmanager.version_id")).IsEqualTo("d6d1b757d46d449d1835a10869dfb9d1");
    }

    [Test]
    public async Task TagReloadActivityWithSecretIdAndVersionId()
    {
        // arrange
        var secretName = NewUniqueSecretName();
        using var listener = StartActivityListener(out var activities);

        var newSecretValueResponse = new GetSecretValueResponse
        {
            VersionId = "d6d1b757d46d449d1835a10869dfb9d2",
            SecretString = """
                {
                    "AppSettingsKey": "Second value"
                }
                """
        };

        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .ReturnsSequentially(InitialSecretValueResponse, newSecretValueResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = secretName, SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        provider.Load();
        provider.Reload();

        // assert
        var activity = FindActivity(activities, ActivityDescriptors.ReloadActivityName, secretName);
        await Assert.That(activity).IsNotNull();
        await Assert.That(GetTag(activity!, "aws.secretsmanager.secret_id")).IsEqualTo(secretName);
        await Assert.That(GetTag(activity!, "aws.secretsmanager.version_id")).IsEqualTo("d6d1b757d46d449d1835a10869dfb9d2");
    }

    [Test]
    public async Task TagSkippedReloadActivityWithSecretIdAndVersionId()
    {
        // arrange
        var secretName = NewUniqueSecretName();
        using var listener = StartActivityListener(out var activities);

        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .ReturnsSequentially(InitialSecretValueResponse, InitialSecretValueResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = secretName, SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        provider.Load();
        provider.Reload();

        // assert
        var activity = FindActivity(activities, ActivityDescriptors.ReloadActivityName, secretName);
        await Assert.That(activity).IsNotNull();
        await Assert.That(GetTag(activity!, "aws.secretsmanager.secret_id")).IsEqualTo(secretName);
        await Assert.That(GetTag(activity!, "aws.secretsmanager.version_id")).IsEqualTo("d6d1b757d46d449d1835a10869dfb9d1");
    }

    // activities are only created when a listener is registered, and listeners are
    // process-global; a unique secret name per test keeps parallel test runs isolated
    private static string NewUniqueSecretName() => $"le-secret-{Guid.NewGuid():N}";

    private static ActivityListener StartActivityListener(out ConcurrentBag<Activity> activities)
    {
        var stopped = new ConcurrentBag<Activity>();

        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivityDescriptors.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stopped.Add(activity),
        };

        ActivitySource.AddActivityListener(listener);

        activities = stopped;
        return listener;
    }

    private static Activity? FindActivity(ConcurrentBag<Activity> activities, string operationName, string secretName) =>
        activities.SingleOrDefault(a =>
            a.OperationName == operationName &&
            GetTag(a, "aws.secretsmanager.secret_id") == secretName);

    private static string? GetTag(Activity activity, string key) =>
        activity.Tags.FirstOrDefault(t => t.Key == key).Value;
}
