using System.Text.RegularExpressions;

namespace PatchPanda.Web.Services;

public class UpdateService
{
    private readonly DockerService _dockerService;
    private readonly IDbContextFactory<DataContext> _dbContextFactory;
    private readonly IFileService _fileService;
    private readonly IPortainerService _portainerService;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(
        DockerService dockerService,
        IDbContextFactory<DataContext> dbContextFactory,
        IFileService fileService,
        ILogger<UpdateService> logger,
        IPortainerService portainerService
    )
    {
        _dockerService = dockerService;
        _dbContextFactory = dbContextFactory;
        _fileService = fileService;
        _portainerService = portainerService;
        _logger = logger;
    }

    public bool IsUpdateAvailable(Container app) =>
        !app.IsSecondary
        || app.Regex is null
        || app.GitHubVersionRegex is null
        || app.Version is null;

    public async Task<List<string>?> Update(
        Container app,
        bool planOnly,
        Action<string>? outputCallback = null,
        AppVersion? targetVersion = null
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
        var configPath = stack.ConfigFile;

        if (configPath is null && (!stack.PortainerManaged || !_portainerService.IsConfigured))
            return null;

        string? configFileContent;

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            updateSteps.Add($"In folder: {configPath}");
            configFileContent = _fileService.ReadAllText(configPath);
        }
        else
        {
            configFileContent = await _portainerService.GetStackFileContentAsync(stack.StackName);

            if (configFileContent is null)
                return null;

            updateSteps.Add("Using Portainer-managed stack file for update");
        }

        var matches = Regex.Matches(configFileContent, app.TargetImage).Count;
        var targetVersionToUse = targetVersion ?? app.NewerVersions.First();
        var newVersion = targetVersionToUse.VersionNumber;
        var adjustedRegex = app.GitHubVersionRegex.TrimStart('^', 'v').TrimEnd('$');
        var versionMatch = Regex.Match(app.Version, adjustedRegex);

        if (versionMatch.Success)
        {
            newVersion = app.Version.Replace(versionMatch.Value, newVersion.TrimStart('v'));
        }
        else
        {
            adjustedRegex = "(" + app.Regex.TrimStart('^').TrimEnd('$') + ")";
            versionMatch = Regex.Match(newVersion, adjustedRegex);

            if (versionMatch.Success)
            {
                var match = Regex.Match(app.Version, app.Regex);

                if (!match.Success)
                    throw new Exception("Could not match versions for update.");

                newVersion = app.Version.Replace(match.Value, versionMatch.Groups[1].Value);
            }
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
                if (string.IsNullOrWhiteSpace(configPath))
                {
                    _logger.LogWarning(
                        "Cannot update .env file for Portainer-managed stack {StackName}.",
                        stack.StackName
                    );
                    return null;
                }

                envFile = Path.Combine(Path.GetDirectoryName(configPath) ?? string.Empty, ".env");

                if (_fileService.Exists(envFile))
                {
                    envFileContent = _fileService.ReadAllText(envFile);
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
            if (string.IsNullOrWhiteSpace(configPath))
            {
                await _portainerService.UpdateStackFileContentAsync(
                    stack.StackName,
                    configFileContent.Replace(app.TargetImage, resultingImage)
                );
            }
            else
            {
                _fileService.WriteAllText(
                    configPath,
                    configFileContent.Replace(app.TargetImage, resultingImage)
                );
            }
        }
        else if (
            envFile is not null
            && envFileContent is not null
            && currentEnvLine is not null
            && targetEnvLine is not null
        )
        {
            _fileService.WriteAllText(
                envFile,
                envFileContent.Replace(currentEnvLine, targetEnvLine)
            );
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            await _dockerService.RunDockerComposeOnPath(stack, "pull", outputCallback);
            await _dockerService.RunDockerComposeOnPath(stack, "down", outputCallback);
            await _dockerService.RunDockerComposeOnPath(stack, "up -d", outputCallback);
        }

        var targetApp = await db
            .Containers.Include(x => x.NewerVersions)
            .FirstAsync(x => x.Id == app.Id);

        var removedVersions = targetApp.NewerVersions.RemoveAll(x =>
            targetVersionToUse.Id == x.Id
            || targetVersionToUse.VersionNumber.IsNewerThan(x.VersionNumber)
        );

        var relatedApps = await db
            .Containers.Include(x => x.NewerVersions)
            .Where(c =>
                c.StackId == targetApp.StackId
                && c.Id != targetApp.Id
                && c.TargetImage == targetApp.TargetImage
            )
            .ToListAsync();

        if (resultingImage is not null)
        {
            targetApp.TargetImage = resultingImage;

            foreach (var related in relatedApps)
            {
                related.TargetImage = resultingImage;

                related.NewerVersions.RemoveAll(x =>
                    targetVersionToUse.Id == x.Id
                    || targetVersionToUse.VersionNumber.IsNewerThan(x.VersionNumber)
                );

                related.Version = newVersion;
            }
        }

        targetApp.Version = newVersion;

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Updated through {UpdateCount} versions from {InitialVersion} to {NewVersion}.",
            removedVersions,
            app.Version,
            newVersion
        );

        return updateSteps;
    }
}
