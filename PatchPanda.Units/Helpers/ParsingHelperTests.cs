using System.Reflection;
using System.Runtime.Serialization;
using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;

namespace PatchPanda.Units.Helpers;

public class ParsingHelperTests
{
    private readonly Mock<ILogger<VersionService>> _logger;
    private readonly Mock<IConfiguration> _configuration;
    private readonly Mock<IAIService> _aiService;

    public ParsingHelperTests()
    {
        _logger = new Mock<ILogger<VersionService>>();
        _configuration = new Mock<IConfiguration>();
        _aiService = new Mock<IAIService>();
    }

    [Fact]
    public async Task SetGitHubRepo_DoesNothing_WhenOverrideIsSet()
    {
        var stack = Helper.GetTestStack();
        var container = stack.Apps[0];
        container.OverrideGitHubRepo = new Tuple<string, string>(
            TestData.GITHUB_OWNER,
            TestData.GITHUB_REPO
        );

        var response = new ContainerListResponse { Image = TestData.IMAGE };

        var versionService = new VersionService(
            _logger.Object,
            _configuration.Object,
            Helper.CreateInMemoryFactory(),
            _aiService.Object
        );

        var logger = new Mock<ILogger>().Object;

        await container.SetGitHubRepo(response, versionService, logger);

        Assert.Equal(
            new Tuple<string, string>(TestData.GITHUB_OWNER, TestData.GITHUB_REPO),
            container.OverrideGitHubRepo
        );
        Assert.Null(container.GitHubRepo);
    }

    [Fact]
    public async Task SetGitHubRepo_DoesNotSet_WhenNoMatchesFound()
    {
        var stack = Helper.GetTestStack(TestData.VERSION, null, TestData.IMAGE);
        var container = stack.Apps[0];

        var response = new ContainerListResponse { Image = TestData.ALPINE_IMAGE };

        var versionService = new VersionService(
            _logger.Object,
            _configuration.Object,
            Helper.CreateInMemoryFactory(),
            _aiService.Object
        );

        var logger = new Mock<ILogger>().Object;

        await container.SetGitHubRepo(response, versionService, logger);

        Assert.Null(container.GitHubRepo);
        Assert.Null(container.GitHubVersionRegex);
    }

    [Fact]
    public async Task SetGitHubRepo_SetsRepoAndRegex_WhenGithubUrlFound()
    {
        var stack = Helper.GetTestStack(TestData.VERSION, null, TestData.IMAGE);
        var container = stack.Apps[0];

        var response = new ContainerListResponse { Image = TestData.GITHUB_URL };

        var release = CreateRelease(TestData.RELEASE_TAG, TestData.GITHUB_URL);

        var versionServiceMock = new Mock<IVersionService>();

        versionServiceMock
            .Setup(vs =>
                vs.GetVersions(
                    It.Is<Tuple<string, string>>(t =>
                        t.Item1 == TestData.GITHUB_OWNER && t.Item2 == TestData.GITHUB_REPO
                    )
                )
            )
            .ReturnsAsync([release]);

        var logger = new Mock<ILogger>().Object;

        await container.SetGitHubRepo(response, versionServiceMock.Object, logger);

        Assert.Equal(
            new Tuple<string, string>(TestData.GITHUB_OWNER, TestData.GITHUB_REPO),
            container.GitHubRepo
        );
        Assert.Equal(
            VersionHelper.BuildRegexFromVersion(TestData.RELEASE_TAG),
            container.GitHubVersionRegex
        );
    }

    [Fact]
    public async Task SetGitHubRepo_PicksBestRepoAndAddsSecondary_WhenMultipleRepos()
    {
        var stack = Helper.GetTestStack(TestData.VERSION, null, TestData.IMAGE);
        var container = stack.Apps[0];

        var response = new ContainerListResponse { Image = TestData.MULTI_IMAGE };

        var relA1 = CreateRelease(TestData.RELEASE_TAG_A, TestData.URL_A);
        var relB1 = CreateRelease(TestData.RELEASE_TAG_B, TestData.URL_B);

        var versionServiceMock = new Mock<IVersionService>();

        versionServiceMock
            .Setup(vs =>
                vs.GetVersions(
                    It.Is<Tuple<string, string>>(t =>
                        t.Item1 == TestData.OWNER_A && t.Item2 == TestData.REPO_A
                    )
                )
            )
            .ReturnsAsync([relA1]);

        versionServiceMock
            .Setup(vs =>
                vs.GetVersions(
                    It.Is<Tuple<string, string>>(t =>
                        t.Item1 == TestData.OWNER_B && t.Item2 == TestData.REPO_B
                    )
                )
            )
            .ReturnsAsync([relB1]);

        var logger = new Mock<ILogger>().Object;

        await container.SetGitHubRepo(response, versionServiceMock.Object, logger);

        Assert.Equal(
            new Tuple<string, string>(TestData.OWNER_A, TestData.REPO_A),
            container.GitHubRepo
        );
        Assert.NotNull(container.SecondaryGitHubRepos);
        Assert.Contains(
            new Tuple<string, string>(TestData.OWNER_B, TestData.REPO_B),
            container.SecondaryGitHubRepos
        );
        Assert.Equal(
            VersionHelper.BuildRegexFromVersion(TestData.RELEASE_TAG_A),
            container.GitHubVersionRegex
        );
    }

    [Fact]
    public async Task SetGitHubRepo_CollapsesDifferentCandidates_WhenReleaseUrlsMatch()
    {
        var stack = Helper.GetTestStack(TestData.VERSION, null, TestData.IMAGE);
        var container = stack.Apps[0];

        var response = new ContainerListResponse
        {
            Image =
                $"ghcr.io/{TestData.GITHUB_OWNER}/{TestData.GITHUB_REPO}:1.0,https://github.com/{TestData.OWNER_B}/{TestData.REPO_B}"
        };

        var sharedRelease = CreateRelease(TestData.RELEASE_TAG, TestData.GITHUB_URL);

        var versionServiceMock = new Mock<IVersionService>();

        versionServiceMock
            .Setup(vs =>
                vs.GetVersions(
                    It.Is<Tuple<string, string>>(t =>
                        t.Item1 == TestData.GITHUB_OWNER && t.Item2 == TestData.GITHUB_REPO
                    )
                )
            )
            .ReturnsAsync([sharedRelease]);

        versionServiceMock
            .Setup(vs =>
                vs.GetVersions(
                    It.Is<Tuple<string, string>>(t =>
                        t.Item1 == TestData.OWNER_B && t.Item2 == TestData.REPO_B
                    )
                )
            )
            .ReturnsAsync([sharedRelease]);

        var logger = new Mock<ILogger>().Object;

        await container.SetGitHubRepo(response, versionServiceMock.Object, logger);

        Assert.Equal(
            new Tuple<string, string>(TestData.GITHUB_OWNER, TestData.GITHUB_REPO),
            container.GitHubRepo
        );
        Assert.Null(container.SecondaryGitHubRepos);
        Assert.Equal(
            VersionHelper.BuildRegexFromVersion(TestData.RELEASE_TAG),
            container.GitHubVersionRegex
        );
    }

    private static Release CreateRelease(string tagName, string url)
    {
        var relType = typeof(Release);
#pragma warning disable SYSLIB0050 // Type or member is obsolete
        var rel = (Release)FormatterServices.GetUninitializedObject(relType)!;
#pragma warning restore SYSLIB0050 // Type or member is obsolete

        var tagField = relType.GetField(
            "<TagName>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        if (tagField is not null)
            tagField.SetValue(rel, tagName);
        else
        {
            var tagProp = relType.GetProperty(
                "TagName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            tagProp?.SetValue(rel, tagName);
        }

        var urlField = relType.GetField(
            "<Url>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        if (urlField is not null)
            urlField.SetValue(rel, url);
        else
        {
            var urlProp = relType.GetProperty(
                "Url",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            urlProp?.SetValue(rel, url);
        }

        return rel;
    }
}
