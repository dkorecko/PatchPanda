using System.Text.RegularExpressions;

namespace PatchPanda.Web.Services;

public class UpdateService
{
    private const int MAX_DOCKER_ROLLBACK_ATTEMPTS = 3;

    private readonly DockerService _dockerService;
    private readonly IDbContextFactory<DataContext> _dbContextFactory;
    private readonly IFileService _fileService;
    private readonly IPortainerService _portainerService;
    private readonly ILogger<UpdateService> _logger;
    private readonly IVersionService _versionService;
    private readonly IAppriseService _appriseService;
    private readonly IDiscordService _discordService;
    private readonly JobRegistry _jobRegistry;

    public UpdateService(
        DockerService dockerService,
        IDbContextFactory<DataContext> dbContextFactory,
        IFileService fileService,
        ILogger<UpdateService> logger,
        IPortainerService portainerService,
        IVersionService versionService,
        IAppriseService appriseService,
        IDiscordService discordService,
        JobRegistry jobRegistry
    )
    {
        _dockerService = dockerService;
        _dbContextFactory = dbContextFactory;
        _fileService = fileService;
        _portainerService = portainerService;
        _logger = logger;
        _versionService = versionService;
        _appriseService = appriseService;
        _discordService = discordService;
        _jobRegistry = jobRegistry;
    }

    public bool IsUpdateAvailable(Container app) =>
        !app.IsSecondary
        || app.Regex is null
        || app.GitHubVersionRegex is null
        || app.Version is null;

    public async Task CheckAllForUpdates()
    {
        using var db = _dbContextFactory.CreateDbContext();

        var containers = await db
            .Containers.Include(x => x.NewerVersions)
            .Where(x => !x.IsSecondary && (x.GitHubRepo != null || x.OverrideGitHubRepo != null))
            .ToListAsync();

        var uniqueRepoGroups = containers
            .GroupBy(x => x.GetGitHubRepo()!)
            .OrderBy(x => x.First().LastVersionCheck);

        foreach (var uniqueRepoGroup in uniqueRepoGroups)
        {
            _logger.LogInformation("Checking unique repo group: {Repo}", uniqueRepoGroup.Key);

            var currentVersionGroups = uniqueRepoGroup
                .GroupBy(x => x.Version)
                .Where(x => x.Key is not null);

            foreach (var currentVersionGroup in currentVersionGroups)
            {
                _logger.LogInformation(
                    "Checking version group: {Repo} {Version}",
                    uniqueRepoGroup.Key,
                    currentVersionGroup.Key
                );
                _logger.LogInformation("Got {Count} containers", currentVersionGroup.Count());

                var mainApp = currentVersionGroup.First();

                _logger.LogInformation(
                    "Selected {MainApp} as main application for getting releases",
                    mainApp.Name
                );
                try
                {
                    var otherApps = currentVersionGroup.Skip(1).ToList();

                    var newerVersions = await _versionService.GetNewerVersions(
                        mainApp,
                        [.. otherApps]
                    );

                    if (newerVersions.Any() || mainApp.NewerVersions.Any(x => !x.Notified))
                    {
                        var container = await db
                            .Containers.Include(x => x.NewerVersions)
                            .FirstAsync(x => x.Id == mainApp.Id);
                        var toNotify = container.NewerVersions.Where(x => !x.Notified).ToList();

                        var fullMessage = NotificationMessageBuilder.Build(
                            mainApp,
                            otherApps,
                            toNotify
                        );
                        int successCount = 0;

                        if (_discordService.IsInitialized)
                        {
                            try
                            {
                                await _discordService.SendRawAsync(fullMessage);
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "Discord notification failed, message {Message}",
                                    ex.Message
                                );
                            }
                        }

                        if (_appriseService.IsInitialized)
                        {
                            try
                            {
                                await _appriseService.SendAsync(fullMessage);
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "Apprise notification failed, message {Message}",
                                    ex.Message
                                );
                            }
                        }

                        if (successCount > 0)
                        {
                            try
                            {
                                foreach (var v in toNotify)
                                    v.Notified = true;

                                await db.SaveChangesAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed saving notified flags");
                            }
                        }
                        else
                            _logger.LogWarning(
                                "All notification attempts have failed, will not be marked as notified."
                            );
                    }
                }
                catch (RateLimitException ex)
                {
                    _logger.LogWarning(
                        "Rate limit {Limit} hit when checking for updates, skipping further checks until {Reset}",
                        ex.Limit,
                        ex.ResetsAt
                    );
                }
            }
        }

        await ProcessAutoUpdates(db);
    }

    private async Task ProcessAutoUpdates(DataContext db)
    {
        var settings = await db.AppSettings.ToDictionaryAsync(x => x.Key, x => x.Value);

        if (!settings.TryGetValue(Constants.SettingsKeys.AUTO_UPDATE_ENABLED, out var enabledStr) || !bool.TryParse(enabledStr, out var enabled) || !enabled)
            return;

        var delayHours = 0;
        
        if (settings.TryGetValue(Constants.SettingsKeys.AUTO_UPDATE_DELAY_HOURS, out var delayStr))
            int.TryParse(delayStr, out delayHours);

        var threshold = DateTime.Now.AddHours(-delayHours);

        var candidates = await db.Containers
            .Include(x => x.NewerVersions)
            .Where(x => x.NewerVersions.Any(
                v => !v.Ignored
                && !v.Breaking
                && v.AIBreaking != true
                && v.IsSuspectedMalicious != true
                && v.DateDiscovered < threshold
            ))
            .ToListAsync();

        foreach (var container in candidates)
        {
            if (_jobRegistry.GetQueuedUpdateForContainer(container.Id) is not null
                || _jobRegistry.GetProcessingUpdateForContainer(container.Id) is not null)
                continue;

            var targetVersion = container.NewerVersions
                .Where(v => v is { Ignored: false, Breaking: false, AIBreaking: not true, IsSuspectedMalicious: not true } && v.DateDiscovered < threshold)
                .OrderByDescending(v => v.VersionNumber, Comparer<string>.Create(VersionHelper.NewerComparison))
                .FirstOrDefault();

            if (targetVersion == null) 
                continue;

            var plan = await Update(container, true, targetVersion: targetVersion);

            if (plan is null)
                continue;
            
            _logger.LogInformation("Auto-queueing update for {Container} to version {Version}", container.Name, targetVersion.VersionNumber);
            await _jobRegistry.MarkForUpdate(container.Id, targetVersion.Id, targetVersion.VersionNumber, true);
        }
    }

    public async Task<List<string>?> Update(
        Container app,
        bool planOnly,
        Action<string>? outputCallback = null,
        AppVersion? targetVersion = null
    )
    {
        if (!IsUpdateAvailable(app))
            throw new Exception("Update is not available.");

        DateTime startedAt = DateTime.UtcNow;

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

        List<Container>? appsWithSharedEnvVersion = null;

        if (matches == 0) // Did not find in main config, check .env
        {
            var mainImageVersionLine = Regex
                .Matches(configFileContent, "\\${([a-zA-Z0-9\\-_]+):-[a-zA-Z0-9\\-_]+}")
                .FirstOrDefault(x =>
                    configFileContent.Contains(app.TargetImage.Split(':')[0] + $":{x.Value}")
                );

            if (mainImageVersionLine is not null)
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
                    var envVarRegex = Regex.Escape(mainImageVersionLine.Groups[1].Value);
                    var targetImageSecondPortion = app.TargetImage.Split(':')[1];
                    currentEnvLine = Regex
                        .Match(
                            envFileContent,
                            $"{envVarRegex}={Regex.Escape(targetImageSecondPortion)}"
                        )
                        .Value;

                    if (!string.IsNullOrWhiteSpace(currentEnvLine))
                    {
                        targetEnvLine = currentEnvLine.Replace(
                            targetImageSecondPortion,
                            newVersion
                        );

                        updateSteps.Add($"Looking at {envFile} .env file");
                        updateSteps.Add(
                            $"Will replace {currentEnvLine} with {targetEnvLine} in the env file"
                        );

                        if (app.MultiContainerAppId is not null)
                        {
                            var targetMultiContainer = await db
                                .MultiContainerApps.Include(x => x.Containers)
                                .FirstAsync(x => x.Id == app.MultiContainerAppId);
                            appsWithSharedEnvVersion =
                            [
                                .. targetMultiContainer.Containers.Where(x =>
                                    x.Id != app.Id
                                    && configFileContent.Contains(
                                        x.TargetImage.Split(':')[0]
                                            + $":{mainImageVersionLine.Value}"
                                    )
                                ),
                            ];

                            if (appsWithSharedEnvVersion.Any())
                                updateSteps.Add(
                                    $"This update will also affect containers: {string.Join(", ", appsWithSharedEnvVersion.Select(x => x.Name))}"
                                );
                        }
                    }
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

        if (updateSteps.Count < Constants.Limits.MINIMUM_UPDATE_STEPS)
            return null;

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
            !string.IsNullOrWhiteSpace(envFile)
            && !string.IsNullOrWhiteSpace(envFileContent)
            && !string.IsNullOrWhiteSpace(currentEnvLine)
            && !string.IsNullOrWhiteSpace(targetEnvLine)
        )
        {
            _fileService.WriteAllText(
                envFile,
                envFileContent.Replace(currentEnvLine, targetEnvLine)
            );
        }

        string? combinedStdOuts = null;
        string? combinedStdErrs = null;

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            bool turnedOff = false;
            try
            {
                (string pullStdOut, string pullStdErr, int pullExitCode) =
                    await _dockerService.RunDockerComposeOnPath(stack, "pull", outputCallback);

                combinedStdOuts += "[PULL STDOUT]\n" + pullStdOut + "\nExit code: " + pullExitCode;
                combinedStdErrs += "[PULL STDERR]\n" + pullStdErr + "\nExit code: " + pullExitCode;

                (string downStdOut, string downStdErr, int downExitCode) =
                    await _dockerService.RunDockerComposeOnPath(stack, "down", outputCallback);

                combinedStdOuts +=
                    "\n[DOWN STDOUT]\n" + downStdOut + "\nExit code: " + downExitCode;
                combinedStdErrs +=
                    "\n[DOWN STDERR]\n" + downStdErr + "\nExit code: " + downExitCode;

                turnedOff = true;
                (string upStdOut, string upStdErr, int upExitCode) =
                    await _dockerService.RunDockerComposeOnPath(stack, "up -d", outputCallback);

                combinedStdOuts += "\n[UP STDOUT]\n" + upStdOut + "\nExit code: " + upExitCode;
                combinedStdErrs += "\n[UP STDERR]\n" + upStdErr + "\nExit code: " + upExitCode;
            }
            catch (DockerCommandException ex)
            {
                _logger.LogError(ex, "Failed to update, rolling back to previous file states");

                if (resultingImage is not null)
                    _fileService.WriteAllText(configPath, configFileContent);
                else if (
                    envFile is not null
                    && envFileContent is not null
                    && currentEnvLine is not null
                    && targetEnvLine is not null
                )
                    _fileService.WriteAllText(envFile, envFileContent);

                if (turnedOff)
                {
                    int attemptCount = 0;

                    string rollbackStdOut = string.Empty;
                    string rollbackStdErr = string.Empty;

                    while (attemptCount < MAX_DOCKER_ROLLBACK_ATTEMPTS)
                    {
                        (string reupStdOut, string reupStdErr, int reupExitCode) =
                            await _dockerService.RunDockerComposeOnPath(
                                stack,
                                "up -d",
                                outputCallback
                            );

                        if (reupExitCode != 0)
                        {
                            rollbackStdOut +=
                                $"\n[ROLLBACK ATTEMPT {attemptCount + 1} STDOUT]\n"
                                + reupStdOut
                                + "\nExit code: "
                                + reupExitCode;
                            rollbackStdErr +=
                                $"\n[ROLLBACK ATTEMPT {attemptCount + 1} STDERR]\n"
                                + reupStdErr
                                + "\nExit code: "
                                + reupExitCode;
                            attemptCount++;
                            continue;
                        }

                        db.UpdateAttempts.Add(
                            new()
                            {
                                StdOut =
                                    ex.StdOut + rollbackStdOut + "\n[UP STDOUT]\n" + reupStdOut,
                                StdErr =
                                    ex.StdErr + rollbackStdErr + "\n[UP STDERR]\n" + reupStdErr,
                                ExitCode = ex.ExitCode,
                                StartedAt = startedAt,
                                EndedAt = DateTime.UtcNow,
                                ContainerId = app.Id,
                                StackId = stack.Id,
                                FailedCommand = ex.Command,
                                UsedPlan = string.Join(", ", updateSteps),
                            }
                        );

                        break;
                    }
                }
                else
                {
                    db.UpdateAttempts.Add(
                        new()
                        {
                            StdOut = ex.StdOut,
                            StdErr = ex.StdErr,
                            ExitCode = ex.ExitCode,
                            StartedAt = startedAt,
                            EndedAt = DateTime.UtcNow,
                            ContainerId = app.Id,
                            StackId = stack.Id,
                            FailedCommand = ex.Command,
                            UsedPlan = string.Join(", ", updateSteps),
                        }
                    );
                }

                await db.SaveChangesAsync();

                throw;
            }
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
        }

        foreach (var related in relatedApps)
        {
            if (resultingImage is not null)
                related.TargetImage = resultingImage;

            related.NewerVersions.RemoveAll(x =>
                targetVersionToUse.Id == x.Id
                || targetVersionToUse.VersionNumber.IsNewerThan(x.VersionNumber)
            );

            related.Version = newVersion;
        }

        targetApp.Version = newVersion;

        if (
            !string.IsNullOrWhiteSpace(envFile)
            && !string.IsNullOrWhiteSpace(envFileContent)
            && !string.IsNullOrWhiteSpace(currentEnvLine)
            && !string.IsNullOrWhiteSpace(targetEnvLine)
            && appsWithSharedEnvVersion is not null
        )
        {
            var sideEffectUpdated = await db
                .Containers.Include(x => x.NewerVersions)
                .Where(c => appsWithSharedEnvVersion.Select(x => x.Id).Contains(c.Id))
                .ToListAsync();

            foreach (var sideApp in sideEffectUpdated)
            {
                sideApp.NewerVersions.RemoveAll(x =>
                    targetVersionToUse.VersionNumber == x.VersionNumber
                    || targetVersionToUse.VersionNumber.IsNewerThan(x.VersionNumber)
                );
                sideApp.Version = newVersion;
            }
        }

        db.UpdateAttempts.Add(
            new()
            {
                StartedAt = startedAt,
                EndedAt = DateTime.UtcNow,
                ContainerId = app.Id,
                StackId = stack.Id,
                UsedPlan = string.Join(", ", updateSteps),
                ExitCode = 0,
                StdErr = combinedStdErrs,
                StdOut = combinedStdOuts,
            }
        );

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
