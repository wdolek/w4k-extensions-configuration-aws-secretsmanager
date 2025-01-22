using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Category("Integration")]
public class ReloadTests
{
    private string TestSecretName { get; set; }

    [SetUp]
    public void Setup()
    {
        var secretName = $"w4k/awssm/fresh-secret/{Guid.NewGuid():N}";
        var secretValue = """
            {
                "Secret": "Joshua"
            }
            """;

        SecretsManagerTestFixture.SecretsManagerClient.CreateSecret(secretName, secretValue).GetAwaiter().GetResult();
        TestSecretName = secretName;
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            SecretsManagerTestFixture.SecretsManagerClient.DeleteSecret(TestSecretName).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // no-op
            TestContext.Out.WriteLine($"Failed to delete the secret: {TestSecretName} - has it been already removed? {ex.Message}");
        }
    }

    [Test]
    public async Task ReloadNewValue()
    {
        // fake time provider is used to control the time continuum - test will poll faster!
        var pollingInterval = TimeSpan.FromSeconds(60);
        var clock = new FakeTimeProvider(DateTimeOffset.Now);

        // flagged by `OnReloadException` callback when reload fails, see configuration below
        var hasReloadFailed = false;

        // build configuration, load secret for the first time
        var config = new ConfigurationBuilder()
            .AddSecretsManager(
                source =>
                {
                    source.SecretsManager = SecretsManagerTestFixture.SecretsManagerClient;
                    source.SecretName = TestSecretName;
                    source.ConfigurationWatcher = new SecretsManagerPollingWatcher(pollingInterval, clock);
                    source.OnReloadException = ctx =>
                    {
                        ctx.Ignore = true;
                        hasReloadFailed = true;
                    };
                })
            .Build();

        var reloadToken = config.GetReloadToken();

        // -> assert initial state
        Assert.That(config["Secret"], Is.EqualTo("Joshua"));

        // arbitrary delay
        await Task.Delay(TimeSpan.FromSeconds(1));
        clock.Advance(pollingInterval.Add(TimeSpan.FromSeconds(1)));

        // -> assert no state change
        Assert.Multiple(
            () =>
            {
                Assert.That(reloadToken.HasChanged, Is.False);
                Assert.That(config["Secret"], Is.EqualTo("Joshua"));
            });

        // update secret
        var newSecretValue = """
            {
                "Secret": "Rosebud"
            }
            """;

        await SecretsManagerTestFixture.SecretsManagerClient.UpdateSecret(TestSecretName, newSecretValue);

        // arbitrary delay
        await Task.Delay(TimeSpan.FromSeconds(1));
        clock.Advance(pollingInterval.Add(TimeSpan.FromSeconds(1)));

        // -> assert new state
        Assert.Multiple(
            () =>
            {
                Assert.That(reloadToken.HasChanged, Is.True);
                Assert.That(config["Secret"], Is.EqualTo("Rosebud"));
            });

        reloadToken = config.GetReloadToken();

        // delete secret
        await SecretsManagerTestFixture.SecretsManagerClient.DeleteSecret(TestSecretName);

        // arbitrary delay
        await Task.Delay(TimeSpan.FromSeconds(1));
        clock.Advance(pollingInterval.Add(TimeSpan.FromSeconds(1)));

        // -> exception not thrown
        // -> assert no state change
        Assert.Multiple(
            () =>
            {
                Assert.That(reloadToken.HasChanged, Is.False);
                Assert.That(config["Secret"], Is.EqualTo("Rosebud"));

                // `OnReloadException` got executed
                Assert.That(hasReloadFailed, Is.True);
            });
    }
}