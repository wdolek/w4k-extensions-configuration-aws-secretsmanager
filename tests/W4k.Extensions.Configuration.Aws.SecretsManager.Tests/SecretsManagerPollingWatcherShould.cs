using Microsoft.Extensions.Time.Testing;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretsManagerPollingWatcherShould
{
    private FakeTimeProvider _timeProvider;

    [SetUp]
    public void SetUp()
    {
        _timeProvider = new FakeTimeProvider();
    }

    [Test]
    public void ThrowWhenStartedTwice()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var refresher = Substitute.For<ISecretsManagerConfigurationProvider>();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act & assert
        watcher.Start(refresher);
        Assert.Throws<InvalidOperationException>(() => watcher.Start(refresher));
    }

    [Test]
    public void ExecuteRefreshAfterInterval()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var refresher = Substitute.For<ISecretsManagerConfigurationProvider>();
        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act
        watcher.Start(refresher);

        // assert
        // 1st refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        refresher.Received(1).Refresh();

        // 2nd refresh
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        refresher.Received(2).Refresh();
    }

    [Test]
    public void SwallowException()
    {
        // arrange
        var interval = TimeSpan.FromMinutes(5);

        var refresher = Substitute.For<ISecretsManagerConfigurationProvider>();
        refresher
            .When(r => r.Refresh())
            .Throw(new InvalidOperationException("Test exception"));

        var watcher = new SecretsManagerPollingWatcher(interval, _timeProvider);

        // act
        watcher.Start(refresher);

        // assert
        // 1st refresh -> exception not thrown
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        refresher
            .Received()
            .Refresh();

        // 2nd refresh -> exception not thrown
        _timeProvider.Advance(interval.Add(TimeSpan.FromSeconds(1)));
        refresher
            .Received()
            .Refresh();
    }
}