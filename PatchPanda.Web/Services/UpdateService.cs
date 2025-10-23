using System.Text.RegularExpressions;

namespace PatchPanda.Web.Services;

public class UpdateService
{
    private readonly DockerService _dockerService;
    private readonly IDbContextFactory<DataContext> _dbContextFactory;

    public UpdateService(
        DockerService dockerService,
        IDbContextFactory<DataContext> dbContextFactory
    )
    {
        _dockerService = dockerService;
        _dbContextFactory = dbContextFactory;
    }

    public bool IsUpdateAvailable(Container app) => !app.IsSecondary;

    public async Task<List<string>> Update(Container app, bool planOnly)
    {
        if (!IsUpdateAvailable(app))
            throw new Exception("Update is not available.");

        using var db = _dbContextFactory.CreateDbContext();
        var stack = await db.Stacks.FirstAsync(x => x.Id == app.StackId);
        var configPath = _dockerService.ComputeConfigFilePath(stack);
        var configFileContent = File.ReadAllText(configPath);

        var matches = Regex.Matches(configFileContent, app.TargetImage).Count;
        var resultingImage =
            app.TargetImage.Split(':')[0] + ':' + app.NewerVersions.First().VersionNumber;

        var updateSteps = new List<string>
        {
            $"In folder: {configPath}",
            $"Will replace {matches} occurrences of {app.TargetImage} and replace them with {resultingImage}",
            $"Pull image for container {app.Name}",
            $"Stop image for container {app.Name}",
            $"Start image for container {app.Name}"
        };

        if (planOnly)
            return updateSteps;

        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace(app.TargetImage, resultingImage)
        );

        await _dockerService.RunDockerComposeOnPath(stack, "pull");
        await _dockerService.RunDockerComposeOnPath(stack, "down");
        await _dockerService.RunDockerComposeOnPath(stack, "up -d");

        app.NewerVersions = [];

        await _dockerService.ResetComposeStacks();

        return updateSteps;
    }
}
