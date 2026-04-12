using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PatchPanda.Units.Services;

public class UpdateServiceTests
{
    private readonly Mock<IFileService> _fileService = new();
    private readonly Mock<ILogger<VersionService>> _versionLogger = new();
    private readonly Mock<ILogger<DockerService>> _dockerLogger = new();
    private readonly Mock<ILogger<UpdateService>> _updateLogger = new();
    private readonly Mock<IConfiguration> _configuration = new();
    private readonly Mock<IPortainerService> _portainerService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IVersionService> _versionService = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly JobRegistry _jobRegistry = new(new());

    private UpdateService CreateUpdateService(IDbContextFactory<DataContext> dbContextFactory)
    {
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
            .Setup(x =>
                x.RunDockerComposeOnPath(
                    It.IsAny<ComposeStack>(),
                    It.IsAny<string>(),
                    It.IsAny<Action<string>?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((string.Empty, string.Empty, 0));

        return new(
            dockerMock.Object,
            dbContextFactory,
            _fileService.Object,
            _updateLogger.Object,
            _portainerService.Object,
            _versionService.Object,
            _jobRegistry,
            _notificationService.Object
        );
    }

    private async Task AssertCustomComposeVersionUpdate(
        ComposeStack stack,
        string expectedImage,
        string expectedVersion,
        string serviceName = "testapp"
    )
    {
        _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileService
            .Setup(systemFileService => systemFileService.ReadAllText(It.IsAny<string>()))
            .Returns(
                $"""
                version: '3'
                services:
                  {serviceName}:
                    image: {stack.Apps[0].TargetImage}
                """
            );

        var dbContextFactory = Helper.CreateInMemoryFactory();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        await CreateUpdateService(dbContextFactory).Update(
            stack.Apps[0],
            false,
            stack.Apps[0].NewerVersions[0]
        );

        await using var dbCheck = await dbContextFactory.CreateDbContextAsync();
        var app = await dbCheck.Containers.Include(x => x.NewerVersions).FirstAsync();

        Assert.Equal(expectedImage, app.TargetImage);
        Assert.Equal(expectedVersion, app.Version);
    }

    private async Task AssertCustomEnvVersionUpdate(
        ComposeStack stack,
        string targetImageLine,
        string parameterName,
        string expectedVersion
    )
    {
        _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileService
            .Setup(systemFileService => systemFileService.ReadAllText("docker-compose.yml"))
            .Returns(
                $"""
                version: '3'
                services:
                  testapp:
                    image: {targetImageLine}
                """
            );

        _fileService
            .Setup(systemFileService => systemFileService.ReadAllText(".env"))
            .Returns(
                $"""
                {parameterName}={stack.Apps[0].Version}
                """
            );

        var dbContextFactory = Helper.CreateInMemoryFactory();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var tasks = await CreateUpdateService(dbContextFactory).Update(
            stack.Apps[0],
            false,
            stack.Apps[0].NewerVersions[0]
        );

        var importantTask = tasks.Steps!.FirstOrDefault(t => t.Contains("Will replace"));

        Assert.NotNull(importantTask);
        Assert.Contains($"{parameterName}={stack.Apps[0].Version}", importantTask);
        Assert.Contains($"{parameterName}={expectedVersion}", importantTask);

        await using var dbCheck = await dbContextFactory.CreateDbContextAsync();
        var app = await dbCheck.Containers.Include(x => x.NewerVersions).FirstAsync();

        Assert.Equal(expectedVersion, app.Version);
    }

    private async Task AssertUpdateFailsForInvalidVersionMapping(
        ComposeStack stack,
        string expectedFailReason,
        string serviceName = "testapp"
    )
    {
        _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileService
            .Setup(systemFileService => systemFileService.ReadAllText(It.IsAny<string>()))
            .Returns(
                $"""
                version: '3'
                services:
                  {serviceName}:
                    image: {stack.Apps[0].TargetImage}
                """
            );

        var dbContextFactory = Helper.CreateInMemoryFactory();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var result = await CreateUpdateService(dbContextFactory).Update(
            stack.Apps[0],
            true,
            stack.Apps[0].NewerVersions[0]
        );

        Assert.NotNull(result.FailReason);
        Assert.Contains(expectedFailReason, result.FailReason);

        await using var dbCheck = await dbContextFactory.CreateDbContextAsync();
        var app = await dbCheck.Containers.Include(x => x.NewerVersions).FirstAsync();

        Assert.Equal(stack.Apps[0].TargetImage, app.TargetImage);
        Assert.Equal(stack.Apps[0].Version, app.Version);
        Assert.Single(app.NewerVersions);
    }

    private async Task GenericTestComposeVersion(ComposeStack stack, string resultImage)
    {
        _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileService
            .Setup(systemFileService => systemFileService.ReadAllText(It.IsAny<string>()))
            .Returns(
                $"""
                version: '3'
                services:
                  testapp:
                    image: {stack.Apps[0].TargetImage}
                """
            );
        var dbContextFactory = Helper.CreateInMemoryFactory();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var tasks = await CreateUpdateService(dbContextFactory).Update(
            stack.Apps[0],
            false,
            stack.Apps[0].NewerVersions[0]
        );

        var importantTask = tasks!.Steps!.FirstOrDefault(t => t.Contains("Will"));

        Assert.NotNull(importantTask);

        Assert.NotEqual(stack.Apps[0].Version, stack.Apps[0].NewerVersions[0].VersionNumber);

        Assert.Contains(stack.Apps[0].TargetImage, importantTask);
        Assert.Contains(resultImage, importantTask);

        await using var dbCheck = await dbContextFactory.CreateDbContextAsync();

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
            .Setup(systemFileService => systemFileService.ReadAllText("docker-compose.yml"))
            .Returns(
                $"""
                version: '3'
                services:
                  testapp:
                    image: {targetImageLine}
                """
            );

        _fileService
            .Setup(systemFileService => systemFileService.ReadAllText(".env"))
            .Returns(
                $"""
                {parameterName}={stack.Apps[0].Version}
                """
            );

        var dbContextFactory = Helper.CreateInMemoryFactory();

        using var db = dbContextFactory.CreateDbContext();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var tasks = await CreateUpdateService(dbContextFactory).Update(
            stack.Apps[0],
            false,
            stack.Apps[0].NewerVersions[0]
        );

        var importantTask = tasks!.Steps!.FirstOrDefault(t => t.Contains("Will"));

        Assert.NotNull(importantTask);

        Assert.NotEqual(stack.Apps[0].Version, stack.Apps[0].NewerVersions[0].VersionNumber);

        Assert.Contains($"{parameterName}={stack.Apps[0].Version}", importantTask);
        Assert.Contains(
            $"{parameterName}={stack.Apps[0].NewerVersions[0].VersionNumber}",
            importantTask
        );

        await using var dbCheck = await dbContextFactory.CreateDbContextAsync();

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
            .Setup(p => p.GetStackFileContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(composeContent);
        _portainerService.Setup(p =>
            p.UpdateStackFileContentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            )
        );

        var dbContextFactory = Helper.CreateInMemoryFactory();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var updateService = CreateUpdateService(dbContextFactory);

        var tasks = await updateService.Update(
            stack.Apps[0],
            false,
            stack.Apps[0].NewerVersions[0]
        );

        Assert.NotNull(tasks);

        _portainerService.Verify(
            p => p.GetStackFileContentAsync(stack.StackName, It.IsAny<CancellationToken>()),
            Times.Once
        );
        _portainerService.Verify(
            p =>
                p.UpdateStackFileContentAsync(
                    stack.StackName,
                    It.Is<string>(s => s.Contains(TestData.IMAGE_NEW_VERSION)),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );

        await using var dbCheck = await dbContextFactory.CreateDbContextAsync();
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
            NewerVersions = [stack.Apps[0].NewerVersions[0]],
        };

        stack.Apps.Add(secondApp);

        _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileService
            .Setup(systemFileService => systemFileService.ReadAllText(It.IsAny<string>()))
            .Returns(
                $"""
                version: '3'
                services:
                  testapp:
                    image: {stack.Apps[0].TargetImage}
                """
            );

        var dbContextFactory = Helper.CreateInMemoryFactory();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        db.Stacks.Add(stack);
        await db.SaveChangesAsync();

        var updateService = CreateUpdateService(dbContextFactory);

        await using var initialCheck = await dbContextFactory.CreateDbContextAsync();
        var hasVersion = await initialCheck
            .Containers.Where(x => x.Id == secondApp.Id)
            .Include(x => x.NewerVersions)
            .AsNoTracking()
            .AnyAsync(x => x.NewerVersions.Count == 1);

        Assert.True(hasVersion);

        var tasks = await updateService.Update(
            stack.Apps[0],
            false,
            stack.Apps[0].NewerVersions[0]
        );

        await using var dbCheck = await dbContextFactory.CreateDbContextAsync();
        var apps = await dbCheck
            .Containers.OrderBy(x => x.Name)
            .Include(container => container.NewerVersions)
            .ToListAsync();

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

    [Fact]
    public async Task UpdateWithPrefixedVersionTag_ShouldReplaceEntireVersionSegment()
    {
        var targetImage = "fireflyiii/core:version-6.5.8";
        await AssertCustomComposeVersionUpdate(
            Helper.GetTestStack(
                "version-6.5.8",
                "v6.5.9",
                targetImage,
                "^\\d+\\.\\d+\\.\\d+$",
                "^v\\d+\\.\\d+$"
            ),
            "fireflyiii/core:version-6.5.9",
            "version-6.5.9",
            "firefly"
        );
    }

    [Fact]
    public async Task UpdateWithPrefixedVersionTagAndLsSuffix_ShouldPreserveSuffix()
    {
        var targetImage = "fireflyiii/core:version-6.5.8-ls54";
        await AssertCustomComposeVersionUpdate(
            Helper.GetTestStack(
                "version-6.5.8-ls54",
                "v6.5.9",
                targetImage,
                "^\\d+\\.\\d+\\.\\d+-ls\\d+$",
                "^v\\d+\\.\\d+$"
            ),
            "fireflyiii/core:version-6.5.9-ls54",
            "version-6.5.9-ls54",
            "firefly"
        );
    }

    [Fact]
    public async Task UpdateWithReleasePrefixAndAlpineSuffix_ShouldPreserveBoth()
    {
        var targetImage = "example/image:release-1.2.3-alpine";
        await AssertCustomComposeVersionUpdate(
            Helper.GetTestStack(
                "release-1.2.3-alpine",
                "v1.2.4",
                targetImage,
                "^\\d+\\.\\d+\\.\\d+-alpine$",
                "^v\\d+\\.\\d+\\.\\d+$"
            ),
            "example/image:release-1.2.4-alpine",
            "release-1.2.4-alpine"
        );
    }

    [Fact]
    public async Task UpdateWithNightlyPrefix_ShouldReplaceDateStyleVersion()
    {
        var targetImage = "example/image:nightly-2024.01.02";
        await AssertCustomComposeVersionUpdate(
            Helper.GetTestStack(
                "nightly-2024.01.02",
                "2024.01.03",
                targetImage,
                "^\\d+\\.\\d+\\.\\d+$",
                "^\\d+\\.\\d+\\.\\d+$"
            ),
            "example/image:nightly-2024.01.03",
            "nightly-2024.01.03"
        );
    }

    [Fact]
    public async Task UpdateWithRPrefix_ShouldReplaceRevisionNumber()
    {
        var targetImage = "example/image:release-r-123";
        await AssertCustomComposeVersionUpdate(
            Helper.GetTestStack(
                "release-r-123",
                "r-124",
                targetImage,
                "^r-\\d+$",
                "^r-\\d+$"
            ),
            "example/image:release-r-124",
            "release-r-124"
        );
    }

    [Fact]
    public async Task UpdateWithVersionPrefixAndRSuffix_ShouldPreserveSuffix()
    {
        var targetImage = "example/image:version-1.2.3-r4";
        await AssertCustomComposeVersionUpdate(
            Helper.GetTestStack(
                "version-1.2.3-r4",
                "v1.2.4",
                targetImage,
                "^\\d+\\.\\d+\\.\\d+-r\\d+$",
                "^v\\d+\\.\\d+\\.\\d+$"
            ),
            "example/image:version-1.2.4-r4",
            "version-1.2.4-r4"
        );
    }

    [Fact]
    public async Task UpdateWithDifferentGithubTagShape_ShouldUseMatchingSegment()
    {
        var targetImage = "example/image:release-1.2.3-alpine";
        await AssertCustomComposeVersionUpdate(
            Helper.GetTestStack(
                "release-1.2.3-alpine",
                "release/v1.2.4",
                targetImage,
                "^\\d+\\.\\d+\\.\\d+-alpine$",
                "^release/v\\d+\\.\\d+\\.\\d+$"
            ),
            "example/image:release-1.2.4-alpine",
            "release-1.2.4-alpine"
        );
    }

    [Fact]
    public async Task UpdateWithFourSegmentVersion_ShouldReplaceAllSegments()
    {
        var targetImage = "example/image:version-1.2.3.4";
        await AssertCustomComposeVersionUpdate(
            Helper.GetTestStack(
                "version-1.2.3.4",
                "v1.2.3.5",
                targetImage,
                "^\\d+\\.\\d+\\.\\d+\\.\\d+$",
                "^v\\d+\\.\\d+\\.\\d+\\.\\d+$"
            ),
            "example/image:version-1.2.3.5",
            "version-1.2.3.5"
        );
    }

    [Fact]
    public async Task UpdateEnvVersionWithPrefixAndRSuffix_ShouldPreserveSuffix()
    {
        await AssertCustomEnvVersionUpdate(
            Helper.GetTestStack(
                "version-1.2.3-r4",
                "v1.2.4",
                "example/image:version-1.2.3-r4",
                "^\\d+\\.\\d+\\.\\d+-r\\d+$",
                "^v\\d+\\.\\d+\\.\\d+$"
            ),
            "example/image:${APP_VERSION:-release}",
            "APP_VERSION",
            "version-1.2.4-r4"
        );
    }

    [Fact]
    public async Task UpdateWithNoMatchingVersionSegment_ShouldFailSafely()
    {
        await AssertUpdateFailsForInvalidVersionMapping(
            Helper.GetTestStack(
                "stable",
                "v1.2.4",
                "example/image:stable",
                "^\\d+\\.\\d+\\.\\d+$",
                "^v\\d+\\.\\d+\\.\\d+$"
            ),
            "Could not determine which part of current version"
        );
    }
}
