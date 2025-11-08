namespace PatchPanda.Units;

public class ContainerTests
{
    [Fact]
    public void GetNewestAvailableVersion_SelectsNewestSemanticVersion()
    {
        var container = DataHelper.GetTestStack();

        container.Apps[0].NewerVersions =
        [
            DataHelper.GetTestAppVersion("v1.1.0"),
            DataHelper.GetTestAppVersion("v1.0.5"),
            DataHelper.GetTestAppVersion("v1.1.2")
        ];

        var newest = container.Apps[0].GetNewestAvailableVersion();

        Assert.Equal("v1.1.2", newest!.VersionNumber);
    }

    [Fact]
    public void GetNewestAvailableVersion_ChoosesHigherSuffixVersion()
    {
        var container = DataHelper.GetTestStack();

        container.Apps[0].NewerVersions =
        [
            DataHelper.GetTestAppVersion("v1.5.3-ls325"),
            DataHelper.GetTestAppVersion("v1.5.3-ls324")
        ];

        var newest = container.Apps[0].GetNewestAvailableVersion();

        Assert.Equal("v1.5.3-ls325", newest!.VersionNumber);
    }

    [Fact]
    public void GetNewestAvailableVersion_HandlesAtStyleAndNumeric()
    {
        var container = DataHelper.GetTestStack();

        container.Apps[0].NewerVersions =
        [
            DataHelper.GetTestAppVersion("1.118.1"),
            DataHelper.GetTestAppVersion("n8n@1.119.2")
        ];

        var newest = container.Apps[0].GetNewestAvailableVersion();

        Assert.Equal("n8n@1.119.2", newest!.VersionNumber);
    }
}
