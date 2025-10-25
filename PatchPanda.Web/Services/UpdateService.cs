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

    public bool IsUpdateAvailable(Container app) =>
        !app.IsSecondary
        || app.Regex is null
        || app.GitHubVersionRegex is null
        || app.Version is null;

    public async Task<List<string>> Update(
        Container app,
        bool planOnly,
        Action<string>? outputCallback = null
    )
    {
        if (!IsUpdateAvailable(app))
            throw new Exception("Update is not available.");

        ArgumentNullException.ThrowIfNull(app.Regex);
        ArgumentNullException.ThrowIfNull(app.GitHubVersionRegex);
        ArgumentNullException.ThrowIfNull(app.Version);

        using var db = _dbContextFactory.CreateDbContext();
        var stack = await db.Stacks.FirstAsync(x => x.Id == app.StackId);
        var configPath = _dockerService.ComputeConfigFilePath(stack);
        var configFileContent = File.ReadAllText(configPath);

        var matches = Regex.Matches(configFileContent, app.TargetImage).Count;

        var targetVersion = app.NewerVersions.First();
        string newVersion = targetVersion.VersionNumber;

        if (!Regex.Match(targetVersion.VersionNumber, app.Regex).Success)
        {
            var githubRegexWithoutSuffixAndPrefix = app
                .GitHubVersionRegex.Replace("^v", "^")
                .TrimEnd('$');
            var match = Regex.Match(app.Version, githubRegexWithoutSuffixAndPrefix);

            if (!match.Success)
                throw new Exception("Could not match versions for update.");

            newVersion = app.Version.Replace(match.Value, newVersion.TrimStart('v'));
        }

        var resultingImage = app.TargetImage.Split(':')[0] + ':' + newVersion;

        var updateSteps = new List<string>
        {
            $"In folder: {configPath}",
            $"Will replace {matches} occurrences of {app.TargetImage} and replace them with {resultingImage}",
            $"Pull images for stack {stack.StackName}",
            $"Stop stack {stack.StackName}",
            $"Start stack {stack.StackName}"
        };

        if (planOnly)
            return updateSteps;

        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace(app.TargetImage, resultingImage)
        );

        await _dockerService.RunDockerComposeOnPath(stack, "pull", outputCallback);
        await _dockerService.RunDockerComposeOnPath(stack, "down", outputCallback);
        await _dockerService.RunDockerComposeOnPath(stack, "up -d", outputCallback);

        app.NewerVersions = [];

        await _dockerService.ResetComposeStacks();

        return updateSteps;
    }
}
