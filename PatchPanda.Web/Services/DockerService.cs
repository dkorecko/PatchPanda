using System.Diagnostics;
using Docker.DotNet;

namespace PatchPanda.Web.Services;

public class DockerService
{
    private string DockerSocket { get; init; }

    public DockerService()
    {
        DockerSocket = "unix:///var/run/docker.sock";

#if DEBUG
        DockerSocket = "npipe://./pipe/docker_engine";
#endif
    }

    private DockerClient GetClient() =>
        new DockerClientConfiguration(new Uri(DockerSocket)).CreateClient();

    public async Task<IList<ContainerListResponse>> GetAllContainers()
    {
        using var dockerClient = GetClient();
        return await dockerClient.Containers.ListContainersAsync(
            new ContainersListParameters { All = true, Limit = 999 }
        );
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
                }

                existingStack.Apps.Add(
                    new ComposeApp
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
                        Uptime = container.Status
                    }
                );
            }
        }

        return stacks;
    }

    public async Task Restart(ComposeStack stack)
    {
        await Process.Start("docker", $"compose -f {stack.ConfigFile} restart").WaitForExitAsync();
    }
}
