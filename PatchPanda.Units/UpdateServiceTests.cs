using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatchPanda.Web.Db;
using PatchPanda.Web.Entities;
using PatchPanda.Web.Helpers;
using PatchPanda.Web.Services;

namespace PatchPanda.Units;

public class UpdateServiceTests
{
    private readonly Mock<SystemFileService> _systemFileService;
    private readonly Mock<ILogger<VersionService>> _versionLogger;
    private readonly Mock<ILogger<DockerService>> _dockerLogger;
    private readonly Mock<IConfiguration> _configuration;

    public UpdateServiceTests()
    {
        _systemFileService = new Mock<SystemFileService>();
        _versionLogger = new Mock<ILogger<VersionService>>();
        _dockerLogger = new Mock<ILogger<DockerService>>();
        _configuration = new Mock<IConfiguration>();
    }

    private IDbContextFactory<DataContext> CreateInMemoryFactory()
    {
        var serviceProvider = new ServiceCollection()
            .AddDbContextFactory<DataContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString())
            )
            .BuildServiceProvider();

        return serviceProvider.GetRequiredService<IDbContextFactory<DataContext>>();
    }

    public static ComposeStack GetTestStack(
        string version,
        string githubNewVersion,
        string targetImage
    ) =>
        new ComposeStack
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
                    GitHubVersionRegex = VersionHelper.BuildRegexFromVersion(githubNewVersion),
                    Version = version,
                    TargetImage = targetImage,
                    StackId = 1,
                    NewerVersions =
                    [
                        new AppVersion
                        {
                            Body = "Testing body",
                            Breaking = false,
                            Name = "Test update",
                            VersionNumber = githubNewVersion,
                            Prerelease = false
                        }
                    ],
                    CurrentSha = "abc123",
                    Uptime = "up"
                }
            ]
        };

    private async Task GenericTestComposeVersion(ComposeStack stack, string resultImage)
    {
        _systemFileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _systemFileService
            .Setup(_systemFileService => _systemFileService.ReadAllText(It.IsAny<string>()))
            .Returns(
                $"""
                version: '3'
                services:
                  testapp:
                    image: {stack.Apps[0].TargetImage}
                """
            );
        var dbContextFactory = CreateInMemoryFactory();

        using var db = dbContextFactory.CreateDbContext();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var tasks = await new UpdateService(
            new DockerService(
                _dockerLogger.Object,
                dbContextFactory,
                new VersionService(_versionLogger.Object, _configuration.Object, dbContextFactory)
            ),
            dbContextFactory,
            _systemFileService.Object
        ).Update(stack.Apps[0], true);

        var importantTask = tasks.FirstOrDefault(t => t.Contains("Will"));

        Assert.NotNull(importantTask);

        Assert.Contains(stack.Apps[0].TargetImage, importantTask);
        Assert.Contains(resultImage, importantTask);
    }

    [Fact]
    public async Task MultipleAppsTest()
    {
        await GenericTestComposeVersion(
            GetTestStack("v0.107.69", "v0.108.0", "adguard/adguardhome:v0.107.69"),
            "adguard/adguardhome:v0.108.0"
        );
        await GenericTestComposeVersion(
            GetTestStack("0.15.4-alpine", "v0.16.2", "henrygd/beszel-agent:0.15.4-alpine"),
            "henrygd/beszel-agent:0.16.2-alpine"
        );
        await GenericTestComposeVersion(
            GetTestStack("0.15.4", "v0.16.2", "henrygd/beszel-agent:0.15.4"),
            "henrygd/beszel-agent:0.16.2"
        );
        //await GenericTest(
        //    GetTestStack("1.118.1", "n8n@1.119.0", "n8nio/n8n:1.118.1"),
        //    "n8nio/n8n:1.119.2"
        //);
        await GenericTestComposeVersion(
            GetTestStack("v1.5.3-ls324", "v1.5.3-ls325", "lscr.io/linuxserver/bazarr:v1.5.3-ls324"),
            "lscr.io/linuxserver/bazarr:v1.5.3-ls325"
        );
    }
}
