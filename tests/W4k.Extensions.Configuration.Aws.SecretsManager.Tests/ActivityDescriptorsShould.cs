using W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class ActivityDescriptorsShould
{
    [Test]
    public async Task HaveSourceVersionMatchingPackageVersion()
    {
        // version is derived from the assembly informational version,
        // with the SourceLink commit suffix (`2.3.0+abc1234`) trimmed
        var version = ActivityDescriptors.Source.Version;

        await Assert.That(version).IsNotNullOrEmpty();
        await Assert.That(version!).DoesNotContain("+");
    }
}
