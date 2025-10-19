using System.Text.RegularExpressions;

namespace PatchPanda.Web.Services;

public class UpdateService
{
    private readonly DockerService _dockerService;
    private readonly DataService _dataService;

    public UpdateService(DockerService dockerService, DataService dataService)
    {
        _dockerService = dockerService;
        _dataService = dataService;
    }

    public bool IsUpdateAvailable(ComposeApp app) =>
        app.FromMultiContainer is null && !app.IsSecondary;

    public async Task<List<string>> Update(ComposeApp app, bool planOnly)
    {
        if (!IsUpdateAvailable(app))
            throw new Exception("Update is not available.");

        var stack = Constants.COMPOSE_APPS!.First(x => x.Apps.Any(a => a.Name == app.Name));
        var configPath = _dockerService.ComputeConfigFilePath(stack);
        var configFileContent = File.ReadAllText(configPath);

        var matches = Regex.Matches(configFileContent, app.TargetImage).Count;
        var resultingImage =
            app.TargetImage.Split(':')[0] + ':' + app.NewerVersions.First().VersionNumber;

        var updateSteps = new List<string>
        {
            $"In folder: {stack.ConfigFile}",
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

        await _dataService.UpdateData();

        return updateSteps;
    }
}
