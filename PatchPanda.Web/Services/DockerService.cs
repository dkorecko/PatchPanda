using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Docker.DotNet;

namespace PatchPanda.Web.Services;

public class DockerService
{
    private string DockerSocket { get; init; }

    private readonly ILogger<DockerService> _logger;
    private readonly IConfiguration _configuration;

    public DockerService(ILogger<DockerService> logger, IConfiguration configuration)
    {
        DockerSocket = "unix:///var/run/docker.sock";

#if DEBUG
        DockerSocket = "npipe://./pipe/docker_engine";
#endif
        _logger = logger;
        _configuration = configuration;
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

    public async Task<IList<ComposeStack>> GetAllComposeStacks()
    {
        var oldDataCopy = Constants.COMPOSE_APPS?.ToList();
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

                var app = new ComposeApp
                {
                    Name = container.Labels.TryGetValue(
                        "com.docker.compose.service",
                        out var appName
                    )
                        ? appName
                        : container.Names.FirstOrDefault() ?? "N/A",
                    Version = container.Labels.TryGetValue(
                        "org.opencontainers.image.version",
                        out var appVersion
                    )
                        ? appVersion
                        : "N/A",
                    GitHubRepo = container.Labels.TryGetValue(
                        "org.opencontainers.image.source",
                        out var appSource
                    )
                        ? appSource
                        : null,
                    CurrentSha = container.ImageID,
                    Uptime = container.Status,
                    TargetImage = container.Image,
                    Regex = string.Empty
                };

                app.Regex = VersionHelper.BuildRegexFromVersion(app.Version);

                string[] containsMap = ["mongo", "redis", "db", "database", "cache", "postgres"];

                if (containsMap.Any(app.Name.Contains))
                    app.IsSecondary = true;

                app.NewerVersions =
                    oldDataCopy
                        ?.FirstOrDefault(x => x.StackName == existingStack.StackName)
                        ?.Apps?.FirstOrDefault(x => x.Name == app.Name)
                        ?.NewerVersions ?? [];

                existingStack.Apps.Add(app);

                if (app.GitHubRepo is null)
                {
                    _logger.LogWarning(
                        "App {AppName} in stack {StackName} does not have a GitHub repository label, json representation: {Json}",
                        app.Name,
                        stackName,
                        JsonSerializer.Serialize(container)
                    );
                }
            }
        }

        stacks.ForEach(MultiContainerAppDetector.FillMultiContainerApps);

        return stacks;
    }

    public string ComputeConfigFilePath(ComposeStack stack)
    {
        var hostPath = _configuration.GetValue<string>("APPS_HOST_PATH");
        string configPath = stack.ConfigFile;

#if !DEBUG
        if (hostPath is null)
            throw new InvalidOperationException("ComposeHostPath configuration is missing.");

        configPath = stack.ConfigFile.Replace(hostPath, "/media/apps");
#endif

        return configPath;
    }

    public async Task RunDockerComposeOnPath(ComposeStack stack, string command)
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

        process.OutputDataReceived += (sender, args) => standardOutput.AppendLine(args.Data);
        process.ErrorDataReceived += (sender, args) => standardError.AppendLine(args.Data);

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
