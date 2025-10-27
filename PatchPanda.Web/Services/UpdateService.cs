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

        List<string> updateSteps = [];

        using var db = _dbContextFactory.CreateDbContext();
        var stack = await db.Stacks.FirstAsync(x => x.Id == app.StackId);
        var configPath = _dockerService.ComputeConfigFilePath(stack);

        updateSteps.Add($"In folder: {configPath}");

        var configFileContent = File.ReadAllText(configPath);

        var matches = Regex.Matches(configFileContent, app.TargetImage).Count;
        var targetVersion = app.NewerVersions.First();
        var newVersion = targetVersion.VersionNumber;
        var adjustedRegex = "(" + app.Regex.TrimStart('^').TrimEnd('$') + ")";
        var versionMatch = Regex.Match(newVersion, adjustedRegex);

        if (versionMatch.Success)
        {
            var match = Regex.Match(app.Version, app.Regex);

            if (!match.Success)
                throw new Exception("Could not match versions for update.");

            newVersion = app.Version.Replace(match.Value, versionMatch.Groups[1].Value);
        }

        string? envFile = null;
        string? envFileContent = null;
        string? resultingImage = null;
        string? currentEnvLine = null;
        string? targetEnvLine = null;

        if (matches == 0) // Did not find in main config, check .env
        {
            var envVariable = Regex.Match(
                configFileContent,
                "\\${([a-zA-Z0-9\\-_]+):-[a-zA-Z0-9\\-_]+}"
            );

            if (
                envVariable.Success
                && configFileContent.Contains(
                    app.TargetImage.Split(':')[0] + $":{envVariable.Value}"
                )
            )
            {
                envFile = Path.Combine(Path.GetDirectoryName(configPath) ?? string.Empty, ".env");

                if (File.Exists(envFile))
                {
                    envFileContent = File.ReadAllText(envFile);
                    var envVarRegex = Regex.Escape(envVariable.Groups[1].Value);
                    var targetImageSecondPortion = app.TargetImage.Split(':')[1];
                    currentEnvLine = Regex
                        .Match(
                            envFileContent,
                            $"{envVarRegex}={Regex.Escape(targetImageSecondPortion)}"
                        )
                        .Value;

                    targetEnvLine = currentEnvLine.Replace(targetImageSecondPortion, newVersion);

                    updateSteps.Add($"Looking at {envFile} .env file");
                    updateSteps.Add(
                        $"Will replace {currentEnvLine} with {targetEnvLine} in the env file"
                    );
                }
            }
        }
        else
        {
            resultingImage = app.TargetImage.Split(':')[0] + ':' + newVersion;

            updateSteps.Add(
                $"Will replace {matches} occurrences of {app.TargetImage} and replace them with {resultingImage}"
            );
        }

        updateSteps.Add($"Pull images for stack {stack.StackName} and restart");

        if (planOnly)
            return updateSteps;

        if (resultingImage is not null)
        {
            File.WriteAllText(
                configPath,
                configFileContent.Replace(app.TargetImage, resultingImage)
            );
        }
        else if (
            envFile is not null
            && envFileContent is not null
            && currentEnvLine is not null
            && targetEnvLine is not null
        )
        {
            File.WriteAllText(envFile, envFileContent.Replace(currentEnvLine, targetEnvLine));
        }

        await _dockerService.RunDockerComposeOnPath(stack, "pull", outputCallback);
        await _dockerService.RunDockerComposeOnPath(stack, "down", outputCallback);
        await _dockerService.RunDockerComposeOnPath(stack, "up -d", outputCallback);

        app.NewerVersions = [];

        await _dockerService.ResetComposeStacks();

        return updateSteps;
    }
}
