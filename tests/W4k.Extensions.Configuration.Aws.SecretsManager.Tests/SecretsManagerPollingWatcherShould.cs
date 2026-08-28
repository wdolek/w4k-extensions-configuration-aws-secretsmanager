using Microsoft.Extensions.Time.Testing;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretsManagerPollingWatcherShould
{
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Now);

    [Test]
    public async Task ThrowWhenStartedTwice()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = ISecretsManagerConfigurationProvider.Mock();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act & assert
        watcher.StartWatching(provider);
        await Assert.That(() => watcher.StartWatching(provider)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ExecuteReloadAfterInterval()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = ISecretsManagerConfigurationProvider.Mock();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act
        watcher.StartWatching(provider);

        // assert
        // 1st refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Once);

        // 2nd refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Exactly(2));
    }

    [Test]
    public async Task NotSwallowException()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = ISecretsManagerConfigurationProvider.Mock();
        provider.Reload().Throws(new InvalidOperationException("Test exception"));

        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act
        watcher.StartWatching(provider);

        // assert
        await Assert.That(() => _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)))).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task NotExecuteReloadAfterSopped()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var provider = ISecretsManagerConfigurationProvider.Mock();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act & assert
        watcher.StartWatching(provider);

        // 1st refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Once);

        // stop watching
        watcher.StopWatching();

        // 2nd refresh should not be called
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        provider.Reload().WasCalled(Times.Once);
    }
}