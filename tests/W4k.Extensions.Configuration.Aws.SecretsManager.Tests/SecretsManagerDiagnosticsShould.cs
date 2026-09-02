using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
        await Assert.That(GetTag(activity!, "aws.secretsmanager.secret.id")).IsEqualTo(secretName);
        await Assert.That(GetTag(activity!, "aws.secretsmanager.secret.version_id")).IsEqualTo("d6d1b757d46d449d1835a10869dfb9d1");
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
        await Assert.That(GetTag(activity!, "aws.secretsmanager.secret.id")).IsEqualTo(secretName);
        await Assert.That(GetTag(activity!, "aws.secretsmanager.secret.version_id")).IsEqualTo("d6d1b757d46d449d1835a10869dfb9d2");
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
        await Assert.That(GetTag(activity!, "aws.secretsmanager.secret.id")).IsEqualTo(secretName);
        await Assert.That(GetTag(activity!, "aws.secretsmanager.secret.version_id")).IsEqualTo("d6d1b757d46d449d1835a10869dfb9d1");
    }

    [Test]
    public async Task CountLoadAttempts()
    {
        // arrange
        var secretName = NewUniqueSecretName();
        using var metrics = new MetricsCollector();

        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(InitialSecretValueResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = secretName, SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        provider.Load();

        // assert
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.loads", secretName)).IsEqualTo(1);
    }

    [Test]
    public async Task CountReloadWhenDataChanges()
    {
        // arrange
        var secretName = NewUniqueSecretName();
        using var metrics = new MetricsCollector();

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
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.reloads", secretName)).IsEqualTo(1);
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.reloads.skipped", secretName)).IsEqualTo(0);
    }

    [Test]
    public async Task CountSkippedReloadWhenVersionIsUnchanged()
    {
        // arrange
        var secretName = NewUniqueSecretName();
        using var metrics = new MetricsCollector();

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
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.reloads.skipped", secretName)).IsEqualTo(1);
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.reloads", secretName)).IsEqualTo(0);
    }

    [Test]
    public async Task CountLoadFailure()
    {
        // arrange
        var secretName = NewUniqueSecretName();
        using var metrics = new MetricsCollector();

        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Throws(new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource { SecretName = secretName, SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        await Assert.That(() => provider.Load()).ThrowsExactly<SecretRetrievalException>();

        // assert
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.loads.failed", secretName)).IsEqualTo(1);
    }

    [Test]
    public async Task CountReloadFailure()
    {
        // arrange
        var secretName = NewUniqueSecretName();
        using var metrics = new MetricsCollector();

        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(
                InitialSecretValueResponse)
            .Then()
            .Throws(new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource { SecretName = secretName, SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        provider.Load();
        await Assert.That(() => provider.Reload()).ThrowsExactly<SecretRetrievalException>();

        // assert
        await Assert.That(metrics.CountMeasurements("w4k.secretsmanager.reloads.failed", secretName)).IsEqualTo(1);
    }

    // activities and meters are only observed when a listener is registered, and
    // listeners are process-global; a unique secret name per test keeps parallel
    // test runs isolated
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
            GetTag(a, "aws.secretsmanager.secret.id") == secretName);

    private static string? GetTag(Activity activity, string key) =>
        activity.Tags.FirstOrDefault(t => t.Key == key).Value;

    private sealed class MetricsCollector : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly ConcurrentQueue<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> _counters = new();

        public MetricsCollector()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == MeterDescriptors.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                _counters.Enqueue((instrument.Name, value, tags.ToArray())));

            _listener.Start();
        }

        public int CountMeasurements(string instrumentName, string secretName) =>
            _counters.Count(m =>
                m.Instrument == instrumentName &&
                HasTag(m.Tags, "aws.secretsmanager.secret.id", secretName));

        public void Dispose() => _listener.Dispose();

        private static bool HasTag(KeyValuePair<string, object?>[] tags, string key, string value) =>
            tags.Any(t => t.Key == key && t.Value as string == value);
    }
}