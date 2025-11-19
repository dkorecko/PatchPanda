using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatchPanda.Web.Db;
using PatchPanda.Web.Entities;
using PatchPanda.Web.Services;

namespace PatchPanda.Units;

public class UpdateServiceTests
{
    private readonly Mock<SystemFileService> _systemFileService;
    private readonly Mock<ILogger<VersionService>> _versionLogger;
    private readonly Mock<ILogger<DockerService>> _dockerLogger;
    private readonly Mock<ILogger<UpdateService>> _updateLogger;
    private readonly Mock<IConfiguration> _configuration;

    public UpdateServiceTests()
    {
        _systemFileService = new Mock<SystemFileService>();
        _versionLogger = new Mock<ILogger<VersionService>>();
        _dockerLogger = new Mock<ILogger<DockerService>>();
        _updateLogger = new Mock<ILogger<UpdateService>>();
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
            new Mock<DockerService>(
                _dockerLogger.Object,
                dbContextFactory,
                new VersionService(_versionLogger.Object, _configuration.Object, dbContextFactory)
            ).Object,
            dbContextFactory,
            _systemFileService.Object,
            _updateLogger.Object
        ).Update(stack.Apps[0], false);

        var importantTask = tasks.FirstOrDefault(t => t.Contains("Will"));

        Assert.NotNull(importantTask);

        Assert.NotEqual(stack.Apps[0].Version, stack.Apps[0].NewerVersions[0].VersionNumber);

        Assert.Contains(stack.Apps[0].TargetImage, importantTask);
        Assert.Contains(resultImage, importantTask);

        using var dbCheck = dbContextFactory.CreateDbContext();

        var app = await dbCheck.Containers.Include(x => x.NewerVersions).FirstAsync();

        Assert.Empty(app.NewerVersions);
        Assert.Equal(resultImage, app.TargetImage);
        Assert.Equal(resultImage.Split(':')[1], app.Version);
    }

    private async Task GenericTestEnvVersion(
        ComposeStack stack,
        string targetImageLine,
        string parameterName
    )
    {
        _systemFileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _systemFileService
            .Setup(_systemFileService => _systemFileService.ReadAllText("docker-compose.yml"))
            .Returns(
                $"""
                version: '3'
                services:
                  testapp:
                    image: {targetImageLine}
                """
            );

        _systemFileService
            .Setup(_systemFileService => _systemFileService.ReadAllText(".env"))
            .Returns(
                $"""
                {parameterName}={stack.Apps[0].Version}
                """
            );

        var dbContextFactory = CreateInMemoryFactory();

        using var db = dbContextFactory.CreateDbContext();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var dockerMock = new Mock<DockerService>(
            _dockerLogger.Object,
            dbContextFactory,
            new VersionService(_versionLogger.Object, _configuration.Object, dbContextFactory)
        );

        dockerMock
            .Setup(x => x.RunDockerComposeOnPath(It.IsAny<ComposeStack>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var tasks = await new UpdateService(
            dockerMock.Object,
            dbContextFactory,
            _systemFileService.Object,
            _updateLogger.Object
        ).Update(stack.Apps[0], false);

        var importantTask = tasks.FirstOrDefault(t => t.Contains("Will"));

        Assert.NotNull(importantTask);

        Assert.NotEqual(stack.Apps[0].Version, stack.Apps[0].NewerVersions[0].VersionNumber);

        Assert.Contains($"{parameterName}={stack.Apps[0].Version}", importantTask);
        Assert.Contains(
            $"{parameterName}={stack.Apps[0].NewerVersions[0].VersionNumber}",
            importantTask
        );

        using var dbCheck = dbContextFactory.CreateDbContext();

        var app = await dbCheck.Containers.Include(x => x.NewerVersions).FirstAsync();

        Assert.Empty(app.NewerVersions);
        Assert.Equal(stack.Apps[0].TargetImage, app.TargetImage);
        Assert.Equal(stack.Apps[0].NewerVersions[0].VersionNumber, app.Version);
    }

    [Fact]
    public async Task MultipleAppsTest()
    {
        await GenericTestComposeVersion(
            DataHelper.GetTestStack("v0.107.69", "v0.108.0", "adguard/adguardhome:v0.107.69"),
            "adguard/adguardhome:v0.108.0"
        );
        await GenericTestComposeVersion(
            DataHelper.GetTestStack(
                "0.15.4-alpine",
                "v0.16.2",
                "henrygd/beszel-agent:0.15.4-alpine"
            ),
            "henrygd/beszel-agent:0.16.2-alpine"
        );
        await GenericTestComposeVersion(
            DataHelper.GetTestStack("0.15.4", "v0.16.2", "henrygd/beszel-agent:0.15.4"),
            "henrygd/beszel-agent:0.16.2"
        );
        await GenericTestComposeVersion(
            DataHelper.GetTestStack("1.118.1", "n8n@1.119.2", "n8nio/n8n:1.118.1"),
            "n8nio/n8n:1.119.2"
        );
        await GenericTestComposeVersion(
            DataHelper.GetTestStack(
                "v1.5.3-ls324",
                "v1.5.3-ls325",
                "lscr.io/linuxserver/bazarr:v1.5.3-ls324"
            ),
            "lscr.io/linuxserver/bazarr:v1.5.3-ls325"
        );

        await GenericTestEnvVersion(
            DataHelper.GetTestStack("v2.2.3", "v2.3.0", "ghcr.io/immich-app/immich-server:v2.2.3"),
            "ghcr.io/immich-app/immich-server:${IMMICH_VERSION:-release}",
            "IMMICH_VERSION"
        );
    }

    [Fact]
    public async Task UpdatePropagatesToMatchingFullImage()
    {
        var stack = DataHelper.GetTestStack(TestData.VERSION, TestData.NEW_VERSION, TestData.IMAGE);
        string sidekick = "sidekick";

        var secondApp = new Container
        {
            Id = 2,
            Name = sidekick,
            Version = TestData.VERSION,
            CurrentSha = TestData.SHA,
            TargetImage = TestData.IMAGE,
            Uptime = TestData.UPTIME,
            Stack = stack,
            StackId = stack.Id,
            Regex = TestData.REGEX,
            GitHubVersionRegex = TestData.REGEX,
            NewerVersions = [stack.Apps[0].NewerVersions[0]]
        };

        stack.Apps.Add(secondApp);

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

        var dockerMock = new Mock<DockerService>(
            _dockerLogger.Object,
            dbContextFactory,
            new VersionService(_versionLogger.Object, _configuration.Object, dbContextFactory)
        );

        dockerMock
            .Setup(x => x.RunDockerComposeOnPath(It.IsAny<ComposeStack>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var updateService = new UpdateService(
            dockerMock.Object,
            dbContextFactory,
            _systemFileService.Object,
            _updateLogger.Object
        );

        using var initialCheck = dbContextFactory.CreateDbContext();
        var hasVersion = await initialCheck
            .Containers.Where(x => x.Id == secondApp.Id)
            .Include(x => x.NewerVersions)
            .AsNoTracking()
            .AnyAsync(x => x.NewerVersions.Count == 1);

        Assert.True(hasVersion);

        var tasks = await updateService.Update(stack.Apps[0], false);

        using var dbCheck = dbContextFactory.CreateDbContext();
        var apps = await dbCheck.Containers.OrderBy(x => x.Name).ToListAsync();

        Assert.Equal(2, apps.Count);

        var updatedMain = apps.First(a => a.Name == stack.Apps[0].Name);
        var updatedSide = apps.First(a => a.Name == sidekick);

        Assert.Equal(TestData.IMAGE_NEW_VERSION, updatedMain.TargetImage);
        Assert.Equal(TestData.NEW_VERSION, updatedMain.Version);

        Assert.Empty(updatedMain.NewerVersions);
        Assert.Empty(updatedSide.NewerVersions);

        Assert.Equal(TestData.IMAGE_NEW_VERSION, updatedSide.TargetImage);
        Assert.Equal(TestData.NEW_VERSION, updatedSide.Version);
    }
}
