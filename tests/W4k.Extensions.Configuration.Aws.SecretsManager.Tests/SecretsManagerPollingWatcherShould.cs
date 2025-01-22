using Microsoft.Extensions.Time.Testing;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretsManagerPollingWatcherShould
{
    private FakeTimeProvider _timeProvider;

    [SetUp]
    public void SetUp()
    {
        _timeProvider = new FakeTimeProvider(DateTimeOffset.Now);
    }

    [Test]
    public void ThrowWhenStartedTwice()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = Substitute.For<ISecretsManagerConfigurationProvider>();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act & assert
        watcher.StartWatching(provider);
        Assert.Throws<InvalidOperationException>(() => watcher.StartWatching(provider));
    }

    [Test]
    public void ExecuteReloadAfterInterval()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = Substitute.For<ISecretsManagerConfigurationProvider>();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act
        watcher.StartWatching(provider);

        // assert
        // 1st refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Received(1).Reload();

        // 2nd refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Received(2).Reload();
    }

    [Test]
    public void NotSwallowException()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = Substitute.For<ISecretsManagerConfigurationProvider>();
        provider
            .When(r => r.Reload())
            .Throw(new InvalidOperationException("Test exception"));

        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act
        watcher.StartWatching(provider);

        // assert
        Assert.Throws<InvalidOperationException>(() => _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1))));
    }

    [Test]
    public void NotExecuteReloadAfterSopped()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = Substitute.For<ISecretsManagerConfigurationProvider>();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act & assert
        watcher.StartWatching(provider);

        // 1st refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Received(1).Reload();
        provider.ClearReceivedCalls();

        // stop watching
        watcher.StopWatching();

        // 2nd refresh should not be called
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.DidNotReceive().Reload();
    }
}