using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PatchPanda.Units.Services;

public class UpdateServiceTests
{
    private readonly Mock<IFileService> _fileService;
    private readonly Mock<ILogger<VersionService>> _versionLogger;
    private readonly Mock<ILogger<DockerService>> _dockerLogger;
    private readonly Mock<ILogger<UpdateService>> _updateLogger;
    private readonly Mock<IConfiguration> _configuration;
    private readonly Mock<IPortainerService> _portainerService;
    private readonly Mock<IAppriseService> _appriseService;
    private readonly Mock<IVersionService> _versionService;
    private readonly Mock<IDiscordService> _discordService;
    private readonly Mock<IAIService> _aiService;

    public UpdateServiceTests()
    {
        _fileService = new Mock<IFileService>();
        _versionLogger = new Mock<ILogger<VersionService>>();
        _dockerLogger = new Mock<ILogger<DockerService>>();
        _updateLogger = new Mock<ILogger<UpdateService>>();
        _configuration = new Mock<IConfiguration>();
        _portainerService = new Mock<IPortainerService>();
        _appriseService = new Mock<IAppriseService>();
        _versionService = new Mock<IVersionService>();
        _discordService = new Mock<IDiscordService>();
        _aiService = new Mock<IAIService>();
    }

    private async Task GenericTestComposeVersion(ComposeStack stack, string resultImage)
    {
        _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileService
            .Setup(_systemFileService => _systemFileService.ReadAllText(It.IsAny<string>()))
            .Returns(
                $"""
                version: '3'
                services:
                  testapp:
                    image: {stack.Apps[0].TargetImage}
                """
            );
        var dbContextFactory = Helper.CreateInMemoryFactory();

        using var db = dbContextFactory.CreateDbContext();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var tasks = await new UpdateService(
            new Mock<DockerService>(
                _dockerLogger.Object,
                dbContextFactory,
                new VersionService(
                    _versionLogger.Object,
                    _configuration.Object,
                    dbContextFactory,
                    _aiService.Object
                ),
                _portainerService.Object,
                _fileService.Object
            ).Object,
            dbContextFactory,
            _fileService.Object,
            _updateLogger.Object,
            _portainerService.Object,
            _versionService.Object,
            _appriseService.Object,
            _discordService.Object
        ).Update(stack.Apps[0], false);

        var importantTask = tasks!.FirstOrDefault(t => t.Contains("Will"));

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
        _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileService
            .Setup(_systemFileService => _systemFileService.ReadAllText("docker-compose.yml"))
            .Returns(
                $"""
                version: '3'
                services:
                  testapp:
                    image: {targetImageLine}
                """
            );

        _fileService
            .Setup(_systemFileService => _systemFileService.ReadAllText(".env"))
            .Returns(
                $"""
                {parameterName}={stack.Apps[0].Version}
                """
            );

        var dbContextFactory = Helper.CreateInMemoryFactory();

        using var db = dbContextFactory.CreateDbContext();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var dockerMock = new Mock<DockerService>(
            _dockerLogger.Object,
            dbContextFactory,
            new VersionService(
                _versionLogger.Object,
                _configuration.Object,
                dbContextFactory,
                _aiService.Object
            ),
            _portainerService.Object,
            _fileService.Object
        );

        dockerMock
            .Setup(x => x.RunDockerComposeOnPath(It.IsAny<ComposeStack>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var tasks = await new UpdateService(
            dockerMock.Object,
            dbContextFactory,
            _fileService.Object,
            _updateLogger.Object,
            _portainerService.Object,
            _versionService.Object,
            _appriseService.Object,
            _discordService.Object
        ).Update(stack.Apps[0], false);

        var importantTask = tasks!.FirstOrDefault(t => t.Contains("Will"));

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
            Helper.GetTestStack("v0.107.69", "v0.108.0", "adguard/adguardhome:v0.107.69"),
            "adguard/adguardhome:v0.108.0"
        );
        await GenericTestComposeVersion(
            Helper.GetTestStack("0.15.4-alpine", "v0.16.2", "henrygd/beszel-agent:0.15.4-alpine"),
            "henrygd/beszel-agent:0.16.2-alpine"
        );
        await GenericTestComposeVersion(
            Helper.GetTestStack("0.15.4", "v0.16.2", "henrygd/beszel-agent:0.15.4"),
            "henrygd/beszel-agent:0.16.2"
        );
        await GenericTestComposeVersion(
            Helper.GetTestStack("1.118.1", "n8n@1.119.2", "n8nio/n8n:1.118.1"),
            "n8nio/n8n:1.119.2"
        );
        await GenericTestComposeVersion(
            Helper.GetTestStack(
                "v1.5.3-ls324",
                "v1.5.3-ls325",
                "lscr.io/linuxserver/bazarr:v1.5.3-ls324"
            ),
            "lscr.io/linuxserver/bazarr:v1.5.3-ls325"
        );

        await GenericTestEnvVersion(
            Helper.GetTestStack("v2.2.3", "v2.3.0", "ghcr.io/immich-app/immich-server:v2.2.3"),
            "ghcr.io/immich-app/immich-server:${IMMICH_VERSION:-release}",
            "IMMICH_VERSION"
        );
    }

    [Fact]
    public async Task PortainerComposeUpdateTest()
    {
        var stack = Helper.GetTestStack(TestData.VERSION, TestData.NEW_VERSION, TestData.IMAGE);
        stack.ConfigFile = null;
        stack.PortainerManaged = true;

        var composeContent = $"""
            version: '3'
            services:
              testapp:
                image: {stack.Apps[0].TargetImage}
            """;

        _portainerService.Setup(p => p.IsConfigured).Returns(true);
        _portainerService
            .Setup(p => p.GetStackFileContentAsync(It.IsAny<string>()))
            .ReturnsAsync(composeContent);
        _portainerService
            .Setup(p => p.UpdateStackFileContentAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var dbContextFactory = Helper.CreateInMemoryFactory();

        using var db = dbContextFactory.CreateDbContext();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var dockerMock = new Mock<DockerService>(
            _dockerLogger.Object,
            dbContextFactory,
            new VersionService(
                _versionLogger.Object,
                _configuration.Object,
                dbContextFactory,
                _aiService.Object
            ),
            _portainerService.Object,
            _fileService.Object
        );

        dockerMock
            .Setup(x => x.RunDockerComposeOnPath(It.IsAny<ComposeStack>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var updateService = new UpdateService(
            dockerMock.Object,
            dbContextFactory,
            _fileService.Object,
            _updateLogger.Object,
            _portainerService.Object,
            _versionService.Object,
            _appriseService.Object,
            _discordService.Object
        );

        var tasks = await updateService.Update(stack.Apps[0], false);

        Assert.NotNull(tasks);

        _portainerService.Verify(p => p.GetStackFileContentAsync(stack.StackName), Times.Once);
        _portainerService.Verify(
            p =>
                p.UpdateStackFileContentAsync(
                    stack.StackName,
                    It.Is<string>(s => s.Contains(TestData.IMAGE_NEW_VERSION))
                ),
            Times.Once
        );

        using var dbCheck = dbContextFactory.CreateDbContext();
        var app = await dbCheck.Containers.Include(x => x.NewerVersions).FirstAsync();

        Assert.Empty(app.NewerVersions);
        Assert.Equal(TestData.IMAGE_NEW_VERSION, app.TargetImage);
    }

    [Fact]
    public async Task UpdatePropagatesToMatchingFullImage()
    {
        var stack = Helper.GetTestStack(TestData.VERSION, TestData.NEW_VERSION, TestData.IMAGE);
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

        _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileService
            .Setup(_systemFileService => _systemFileService.ReadAllText(It.IsAny<string>()))
            .Returns(
                $"""
                version: '3'
                services:
                  testapp:
                    image: {stack.Apps[0].TargetImage}
                """
            );

        var dbContextFactory = Helper.CreateInMemoryFactory();

        using var db = dbContextFactory.CreateDbContext();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var dockerMock = new Mock<DockerService>(
            _dockerLogger.Object,
            dbContextFactory,
            new VersionService(
                _versionLogger.Object,
                _configuration.Object,
                dbContextFactory,
                _aiService.Object
            ),
            _portainerService.Object,
            _fileService.Object
        );

        dockerMock
            .Setup(x => x.RunDockerComposeOnPath(It.IsAny<ComposeStack>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var updateService = new UpdateService(
            dockerMock.Object,
            dbContextFactory,
            _fileService.Object,
            _updateLogger.Object,
            _portainerService.Object,
            _versionService.Object,
            _appriseService.Object,
            _discordService.Object
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
