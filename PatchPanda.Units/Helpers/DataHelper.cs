using PatchPanda.Web.Entities;
using PatchPanda.Web.Helpers;

namespace PatchPanda.Units.Helpers;

public static class DataHelper
{
    public static AppVersion GetTestAppVersion(string githubVersion) =>
        new AppVersion
        {
            Body = "Testing body",
            Breaking = false,
            Name = "Test update",
            VersionNumber = githubVersion,
            Prerelease = false
        };

    public static ComposeStack GetTestStack(
        string version,
        string? githubNewVersion,
        string targetImage
    )
    {
        var stack = new ComposeStack
        {
            Id = 1,
            StackName = "TestStack",
            ConfigFile = "docker-compose.yml",
            Apps =
            [
                new Container
                {
                    Id = 1,
                    Name = "TestApp",
                    IsSecondary = false,
                    Regex = VersionHelper.BuildRegexFromVersion(version),
                    GitHubVersionRegex = githubNewVersion is null
                        ? null
                        : VersionHelper.BuildRegexFromVersion(githubNewVersion),
                    Version = version,
                    TargetImage = targetImage,
                    StackId = 1,
                    NewerVersions = [],
                    CurrentSha = "abc123",
                    Uptime = "up"
                }
            ]
        };

        if (githubNewVersion is not null)
            stack.Apps[0].NewerVersions.Add(GetTestAppVersion(githubNewVersion));

        return stack;
    }

    public static ComposeStack GetTestStack() => GetTestStack("1.0.0", "1.1.0", "test/image:1.0.0");
}
