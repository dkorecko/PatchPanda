using System.Diagnostics;
using Docker.DotNet;

namespace PatchPanda.Web.Services;

public class DockerService
{
    private string DockerSocket { get; init; }

    private string AppsHostPath { get; init; }

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
                        : "N/A",
                    CurrentSha = container.ImageID,
                    Uptime = container.Status,
                    Regex = string.Empty
                };

                app.Regex = VersionHelper.BuildRegexFromVersion(app.Version);

                existingStack.Apps.Add(app);
            }
        }

        return stacks;
    }

    public async Task Restart(ComposeStack stack)
    {
        var hostPath = _configuration.GetValue<string>("APPS_HOST_PATH");

        if (hostPath is null)
            throw new InvalidOperationException("ComposeHostPath configuration is missing.");

        await Process
            .Start(
                "docker",
                $"compose -f {stack.ConfigFile.Replace(hostPath, "/media/apps")} restart"
            )
            .WaitForExitAsync();

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f {stack.ConfigFile.Replace(hostPath, "/media/apps")} restart",
            // Key settings for capturing output
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false, // Must be false when redirecting streams
            CreateNoWindow = true // Optional: useful for background processes
        };

        using (var process = new Process { StartInfo = startInfo })
        {
            // StringBuilders to hold the output
            var standardOutput = new System.Text.StringBuilder();
            var standardError = new System.Text.StringBuilder();

            // Event handlers to capture output asynchronously
            process.OutputDataReceived += (sender, args) => standardOutput.AppendLine(args.Data);
            process.ErrorDataReceived += (sender, args) => standardError.AppendLine(args.Data);

            process.Start();

            // Start asynchronous reading of the output streams
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for the process to exit
            await process.WaitForExitAsync();

            // Now, standardOutput.ToString() and standardError.ToString() contain the logs.
            // You can write these to a file, database, or your application's log sink.

            // Example: Log to console/file
            _logger.LogInformation("--- STDOUT ---");
            _logger.LogInformation(standardOutput.ToString());
            _logger.LogInformation("--- STDERR ---");
            _logger.LogInformation(standardError.ToString());
        }
    }
}
