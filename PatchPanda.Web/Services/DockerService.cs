using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Docker.DotNet;

namespace PatchPanda.Web.Services;

public class DockerService
{
    private string DockerSocket { get; init; }

    private readonly ILogger<DockerService> _logger;
    private readonly IDbContextFactory<DataContext> _dbContextFactory;
    private readonly VersionService _versionService;

    public DockerService(
        ILogger<DockerService> logger,
        IDbContextFactory<DataContext> dbContextFactory,
        VersionService versionService
    )
    {
        DockerSocket = "unix:///var/run/docker.sock";

#if DEBUG
        DockerSocket = "npipe://./pipe/docker_engine";
#endif
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _versionService = versionService;
    }

    private DockerClient GetClient() =>
        new DockerClientConfiguration(new Uri(DockerSocket)).CreateClient();

    public async Task<IList<ContainerListResponse>> GetAllContainers()
    {
        using var dockerClient = GetClient();

        var containers = await dockerClient.Containers.ListContainersAsync(
            new ContainersListParameters { All = true, Limit = 999 }
        );

        return containers;
    }

    public async Task<List<ComposeStack>> GetRunningStacks()
    {
        var containers = await GetAllContainers();

        List<ComposeStack> stacks = [];

        foreach (var container in containers)
        {
            if (
                container.Labels.TryGetValue("com.docker.compose.project", out var stackName)
                && container.Labels.TryGetValue(
                    "com.docker.compose.config-hash",
                    out var configHash
                )
            )
            {
                var existingStack = stacks.FirstOrDefault(s => s.StackName == stackName);

                if (existingStack == null)
                {
                    existingStack = new ComposeStack
                    {
                        StackName = stackName,
                        ConfigFile = container.Labels.TryGetValue(
                            "com.docker.compose.project.config_files",
                            out var configFile
                        )
                            ? configFile
                            : "N/A"
                    };

                    stacks.Add(existingStack);

                    _logger.LogInformation(
                        "Found new compose stack: {StackName} (Config Hash: {ConfigHash})",
                        stackName,
                        configHash
                    );
                }

                var app = new Container
                {
                    Name = container.Labels.TryGetValue(
                        "com.docker.compose.service",
                        out var appName
                    )
                        ? appName
                        : container.Names.FirstOrDefault() ?? "N/A",
                    Version = container.Image.Contains(':')
                        ? container.Image.Split(':', 2)[1]
                        : null,
                    CurrentSha = container.ImageID,
                    Uptime = container.Status,
                    TargetImage = container.Image,
                    Regex = string.Empty,
                    Stack = existingStack,
                    StackId = existingStack.Id,
                };

                if (
                    app.Version is null
                    || app.Version.StartsWith("latest")
                    || app.Version.StartsWith("sha")
                )
                {
                    app.Version = container.Labels.TryGetValue(
                        "org.opencontainers.image.version",
                        out var appVersion
                    )
                        ? appVersion
                        : null;
                }

                app.Regex = app.Version is not null
                    ? VersionHelper.BuildRegexFromVersion(app.Version)
                    : null;

                await app.SetGitHubRepo(container, _versionService, _logger);

                string[] containsMap =
                [
                    "mongo",
                    "redis",
                    "db",
                    "database",
                    "cache",
                    "postgres",
                    "broker"
                ];

                if (containsMap.Any(app.Name.Contains))
                    app.IsSecondary = true;

                existingStack.Apps.Add(app);

                if (
                    app.GitHubRepo is null
                    || app.Version is null
                    || app.Regex is null
                    || app.GitHubVersionRegex is null
                )
                {
                    _logger.LogWarning(
                        "App {AppName} in stack {StackName} does not have GitHub repo/version/regex, json representation: {Json}",
                        app.Name,
                        stackName,
                        JsonSerializer.Serialize(container)
                    );
                }
            }
        }

        return stacks;
    }

    public async Task ResetComposeStacks()
    {
        using var db = _dbContextFactory.CreateDbContext();

        var existingStacks = await db
            .Stacks.Include(x => x.Apps)
            .ThenInclude(x => x.NewerVersions)
            .ToListAsync();

        var runningStacks = await GetRunningStacks();
        var foundStacks = new List<ComposeStack>();

        foreach (var runningStack in runningStacks)
        {
            var existingStack = existingStacks.FirstOrDefault(x =>
                runningStack.StackName == x.StackName && runningStack.ConfigFile == x.ConfigFile
            );

            if (existingStack is null)
            {
                db.Stacks.Add(runningStack);
                continue;
            }

            foundStacks.Add(existingStack);

            foreach (var runningContainer in runningStack.Apps)
            {
                var existingContainer = existingStack
                    .Apps.Where(x => x.Name == runningContainer.Name)
                    .FirstOrDefault();
                var foundApps = new List<Container>();

                if (existingContainer is not null)
                {
                    existingContainer.Uptime = runningContainer.Uptime;
                    existingContainer.CurrentSha = runningContainer.CurrentSha;
                    existingContainer.GitHubRepo = runningContainer.GitHubRepo;
                    existingContainer.SecondaryGitHubRepos = runningContainer.SecondaryGitHubRepos;
                    existingContainer.Version = runningContainer.Version;
                    existingContainer.TargetImage = runningContainer.TargetImage;
                    existingContainer.Regex = runningContainer.Regex;
                    existingContainer.GitHubVersionRegex = runningContainer.GitHubVersionRegex;

                    if (runningContainer.Version is not null)
                        existingContainer.NewerVersions.RemoveAll(x =>
                            !x.VersionNumber.IsNewerThan(runningContainer.Version)
                        );

                    foundApps.Add(existingContainer);
                }
                else
                    existingStack.Apps.Add(runningContainer);

                foundApps
                    .Except(foundApps)
                    .ToList()
                    .ForEach(app =>
                    {
                        db.Containers.Remove(app);
                    });
            }
        }

        existingStacks
            .Except(foundStacks)
            .ToList()
            .ForEach(stack =>
            {
                db.Containers.RemoveRange(stack.Apps);
                db.Stacks.Remove(stack);
            });

        db.MultiContainerApps.RemoveRange(db.MultiContainerApps);

        await db.SaveChangesAsync();

        var stacks = await db.Stacks.Include(x => x.Apps).ToListAsync();

        stacks.ForEach(x => MultiContainerAppDetector.FillMultiContainerApps(x, db));

        await db.SaveChangesAsync();
    }

    public string ComputeConfigFilePath(ComposeStack stack)
    {
        string configPath = stack.ConfigFile;
        return configPath;
    }

    public async Task RunDockerComposeOnPath(
        ComposeStack stack,
        string command,
        Action<string>? outputCallback = null
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f {ComputeConfigFilePath(stack)} {command}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (sender, args) =>
        {
            if (args.Data != null)
            {
                standardOutput.AppendLine(args.Data);
                outputCallback?.Invoke(args.Data);
            }
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (args.Data != null)
            {
                standardError.AppendLine(args.Data);
                outputCallback?.Invoke(args.Data);
            }
        };

        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        var stdOut = standardOutput.ToString();
        var stdErr = standardError.ToString();

        if (!string.IsNullOrWhiteSpace(stdOut))
        {
            _logger.LogInformation("--- STDOUT ---");
            _logger.LogInformation(standardOutput.ToString());
        }

        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            _logger.LogInformation("--- STDERR ---");
            _logger.LogInformation(standardError.ToString());
        }
    }
}
