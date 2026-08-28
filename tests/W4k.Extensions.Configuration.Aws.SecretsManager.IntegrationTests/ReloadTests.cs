using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Property("Category", "Integration")]
[NotInParallel]
public class ReloadTests
{
    private string TestSecretName { get; set; } = null!;

    [Before(Test)]
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

    [After(Test)]
    public async Task TearDown(TestContext context)
    {
        try
        {
            await SecretsManagerTestFixture.SecretsManagerClient.DeleteSecret(TestSecretName);
        }
        catch (Exception ex)
        {
            // no-op
            context.Output.WriteLine($"Failed to delete the secret: {TestSecretName} - has it been already removed? {ex.Message}");
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
                TestSecretName,
                source => source
                    .WithSecretsManager(SecretsManagerTestFixture.SecretsManagerClient)
                    .WithPollingWatcher(pollingInterval, clock)
                    .OnReloadException(
                        ctx =>
                        {
                            ctx.Ignore = true;
                            hasReloadFailed = true;
                        }))
            .Build();

        var reloadToken = config.GetReloadToken();

        // -> assert initial state
        await Assert.That(config["Secret"]).IsEqualTo("Joshua");

        // arbitrary delay
        await Task.Delay(TimeSpan.FromSeconds(1));
        clock.Advance(pollingInterval.Add(TimeSpan.FromSeconds(1)));

        // -> assert no state change
        await Assert.That(reloadToken.HasChanged).IsFalse();
        await Assert.That(config["Secret"]).IsEqualTo("Joshua");

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
        await Assert.That(reloadToken.HasChanged).IsTrue();
        await Assert.That(config["Secret"]).IsEqualTo("Rosebud");

        reloadToken = config.GetReloadToken();

        // delete secret
        await SecretsManagerTestFixture.SecretsManagerClient.DeleteSecret(TestSecretName);

        // arbitrary delay
        await Task.Delay(TimeSpan.FromSeconds(1));
        clock.Advance(pollingInterval.Add(TimeSpan.FromSeconds(1)));

        // -> exception not thrown
        // -> assert no state change
        await Assert.That(reloadToken.HasChanged).IsFalse();
        await Assert.That(config["Secret"]).IsEqualTo("Rosebud");

        // `OnReloadException` got executed
        await Assert.That(hasReloadFailed).IsTrue();
    }
}