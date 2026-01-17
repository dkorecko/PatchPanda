using System.Text.RegularExpressions;

namespace PatchPanda.Web.Services;

public class UpdateService
{
    private const int MaxRollbackAttempts = 3;

    private readonly DockerService _dockerService;
    private readonly IDbContextFactory<DataContext> _dbContextFactory;
    private readonly IFileService _fileService;
    private readonly IPortainerService _portainerService;
    private readonly ILogger<UpdateService> _logger;
    private readonly IVersionService _versionService;
    private readonly JobRegistry _jobRegistry;
    private readonly INotificationService _notificationService;

    public UpdateService(
        DockerService dockerService,
        IDbContextFactory<DataContext> dbContextFactory,
        IFileService fileService,
        ILogger<UpdateService> logger,
        IPortainerService portainerService,
        IVersionService versionService,
        JobRegistry jobRegistry,
        INotificationService notificationService
    )
    {
        _dockerService = dockerService;
        _dbContextFactory = dbContextFactory;
        _fileService = fileService;
        _portainerService = portainerService;
        _logger = logger;
        _versionService = versionService;
        _jobRegistry = jobRegistry;
        _notificationService = notificationService;
    }

    private void CleanUpContainerJobs(int containerId)
    {
        var queuedSequence = _jobRegistry.GetQueuedUpdateForContainer(containerId);
        if (queuedSequence.HasValue)
            _jobRegistry.TryRemove(queuedSequence.Value);

        var processingSequence = _jobRegistry.GetProcessingUpdateForContainer(containerId);
        if (processingSequence.HasValue)
            _jobRegistry.FinishProcessing(processingSequence.Value);
    }

    private void CleanUpAllContainerJobs(
        int mainContainerId,
        List<Container>? relatedApps,
        List<Container>? sideEffectUpdated
    )
    {
        CleanUpContainerJobs(mainContainerId);

        if (relatedApps is not null)
        {
            foreach (var related in relatedApps)
            {
                CleanUpContainerJobs(related.Id);
            }
        }

        if (sideEffectUpdated is not null)
        {
            foreach (var sideApp in sideEffectUpdated)
            {
                CleanUpContainerJobs(sideApp.Id);
            }
        }
    }

    public bool IsUpdateAvailable(Container app) =>
        !app.IsSecondary
        || app.Regex is null
        || app.GitHubVersionRegex is null
        || app.Version is null;

    public async Task CheckAllForUpdates()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var containers = await db
            .Containers.Include(x => x.NewerVersions)
            .Where(x => !x.IsSecondary && (x.GitHubRepo != null || x.OverrideGitHubRepo != null))
            .ToListAsync();

        var uniqueRepoGroups = containers
            .GroupBy(x => x.GetGitHubRepo()!)
            .OrderBy(x => x.First().LastVersionCheck);

        foreach (var uniqueRepoGroup in uniqueRepoGroups)
        {
            _logger.LogDebug("Checking unique repo group: {Repo}", uniqueRepoGroup.Key);

            var currentVersionGroups = uniqueRepoGroup
                .GroupBy(x => x.Version)
                .Where(x => x.Key is not null);

            foreach (var currentVersionGroup in currentVersionGroups)
            {
                _logger.LogDebug(
                    "Checking version group: {Repo} {Version}",
                    uniqueRepoGroup.Key,
                    currentVersionGroup.Key
                );
                _logger.LogDebug("Got {Count} containers", currentVersionGroup.Count());

                var mainApp = currentVersionGroup.First();

                _logger.LogDebug(
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

                        var result = await _notificationService.SendNewVersion(
                            mainApp,
                            otherApps,
                            toNotify
                        );

                        if (result)
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

        if (
            !settings.TryGetValue(Constants.SettingsKeys.AUTO_UPDATE_ENABLED, out var enabledStr)
            || !bool.TryParse(enabledStr, out var enabled)
            || !enabled
        )
            return;

        var delayHours = 0;

        if (settings.TryGetValue(Constants.SettingsKeys.AUTO_UPDATE_DELAY_HOURS, out var delayStr))
            int.TryParse(delayStr, out delayHours);

        var threshold = DateTime.Now.AddHours(-delayHours);

        var candidates = await db
            .Containers.Include(x => x.NewerVersions)
            .Where(x =>
                x.NewerVersions.Any(v =>
                    !v.Ignored
                    && !v.Breaking
                    && v.AIBreaking != true
                    && v.IsSuspectedMalicious != true
                    && v.DateDiscovered < threshold
                )
            )
            .ToListAsync();

        foreach (var container in candidates)
        {
            if (
                _jobRegistry.GetQueuedUpdateForContainer(container.Id) is not null
                || _jobRegistry.GetProcessingUpdateForContainer(container.Id) is not null
            )
                continue;

            var allNewerVersions = container
                .NewerVersions.Where(v => !v.Ignored && v.DateDiscovered < threshold)
                .OrderBy(
                    v => v.VersionNumber,
                    Comparer<string>.Create(VersionHelper.NewerComparison)
                )
                .ToList();

            var targetVersion = allNewerVersions.FirstOrDefault(v =>
                !v.Breaking && v.AIBreaking != true && v.IsSuspectedMalicious != true
            );

            if (targetVersion == null)
                continue;

            var plan = await Update(container, true, targetVersion);

            if (plan.Steps is null)
                continue;

            _logger.LogInformation(
                "Auto-queueing update for {Container} to version {Version}",
                container.Name,
                targetVersion.VersionNumber
            );
            await _jobRegistry.MarkForUpdate(
                container.Id,
                targetVersion.Id,
                targetVersion.VersionNumber,
                true
            );
        }
    }

    private UpdatePlanModel HandleError(string errorMessage, bool planOnly)
    {
        if (!planOnly)
            throw new InvalidOperationException(errorMessage);
        return new(errorMessage);
    }

    private string LogAndGetRollbackFailedMessage()
    {
        string message =
            "[ROLLBACK FAILED] Rollback failed after maximum attempts. Manual intervention may be required to restore the application to a stable state.";

        _logger.LogError(message);

        return message;
    }

    public async Task<UpdatePlanModel> Update(
        Container app,
        bool planOnly,
        AppVersion targetVersion,
        Action<string>? outputCallback = null,
        bool isAutomatic = false
    )
    {
        if (!IsUpdateAvailable(app))
            return new(
                "App is missing required information such as GitHub repo, version regex or is secondary (DB, etc.)."
            );

        DateTime startedAt = DateTime.UtcNow;
        List<string> updateSteps = [];
        string rollbackStdOut = string.Empty;
        string rollbackStdErr = string.Empty;
        List<Container>? relatedApps = null;
        List<Container>? sideEffectUpdated = null;
        var rollbackFailed = false;

        try
        {
            ArgumentNullException.ThrowIfNull(app.Regex);
            ArgumentNullException.ThrowIfNull(app.GitHubVersionRegex);
            ArgumentNullException.ThrowIfNull(app.Version);

            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var stack = await db.Stacks.FirstAsync(x => x.Id == app.StackId);
            var configPath = stack.ConfigFile;

            if (configPath is null && (!stack.PortainerManaged || !_portainerService.IsConfigured))
                return HandleError(
                    "Did not get config path and Portainer integration is disabled.",
                    planOnly
                );

            string? configFileContent;

            if (!string.IsNullOrWhiteSpace(configPath))
            {
                updateSteps.Add($"In folder: {configPath}");
                configFileContent = _fileService.ReadAllText(configPath);
            }
            else
            {
                configFileContent = await _portainerService.GetStackFileContentAsync(
                    stack.StackName
                );

                if (configFileContent is null)
                    return HandleError("Failed to get content from Portainer.", planOnly);

                updateSteps.Add("Using Portainer-managed stack file for update");
            }

            var matches = Regex.Matches(configFileContent, app.TargetImage).Count;
            var newVersion = targetVersion.VersionNumber;
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
                        return HandleError(
                            $"Could not match version pattern '{app.Regex}' against current version '{app.Version}' while updating to target version '{newVersion}'.",
                            planOnly
                        );

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
                        return HandleError(
                            "Cannot update .env file for Portainer-managed stacks.",
                            planOnly
                        );
                    }

                    envFile = Path.Combine(
                        Path.GetDirectoryName(configPath) ?? string.Empty,
                        ".env"
                    );

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
            {
                _logger.LogWarning(
                    "Did not generate a valid update plan, actually generated: {Steps}\n"
                        + "===================================================\n"
                        + "configFileContent: {ConfigFileContent}\nmatches: {Matches}, newVersion: {NewVersion}, adjustedRegex: {AdjustedRegex}, versionMatch: {VersionMatch}, resultingImage: {ResultingImage}\n"
                        + "envFile: {EnvFile}\nenvFileContent: {EnvFileContent}\ncurrentEnvLine: {CurrentEnvLine}\ntargetEnvLine: {TargetEnvLine}\n"
                        + "===================================================",
                    updateSteps,
                    configFileContent,
                    matches,
                    newVersion,
                    adjustedRegex,
                    versionMatch.Success,
                    resultingImage,
                    envFile,
                    envFileContent,
                    currentEnvLine,
                    targetEnvLine
                );
                return HandleError(
                    $"Did not generate a valid update plan. Update plan has fewer than {Constants.Limits.MINIMUM_UPDATE_STEPS} steps ({updateSteps.Count}).",
                    planOnly
                );
            }

            if (planOnly)
                return new(updateSteps);

            if (resultingImage is not null)
            {
                if (string.IsNullOrWhiteSpace(configPath))
                {
                    try
                    {
                        await _portainerService.UpdateStackFileContentAsync(
                            stack.StackName,
                            configFileContent.Replace(app.TargetImage, resultingImage)
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to update, rolling back to previous file states"
                        );

                        int attemptCount = 0;

                        while (attemptCount < MaxRollbackAttempts)
                        {
                            if (attemptCount > 0)
                            {
                                var delayMs = (int)Math.Pow(2, attemptCount) * 1000;
                                await Task.Delay(delayMs);
                            }

                            try
                            {
                                await _portainerService.UpdateStackFileContentAsync(
                                    stack.StackName,
                                    configFileContent
                                );

                                rollbackStdErr += $"\n[ROLLBACK SUCCESS]\n";
                                break;
                            }
                            catch (Exception rollbackException)
                            {
                                rollbackStdErr +=
                                    $"\n[ROLLBACK ATTEMPT {attemptCount + 1} STDERR]\n"
                                    + rollbackException;
                                attemptCount++;
                            }
                        }

                        if (attemptCount >= MaxRollbackAttempts)
                            rollbackFailed = true;

                        throw;
                    }
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
                try
                {
                    (string pullStdOut, string pullStdErr, int pullExitCode) =
                        await _dockerService.RunDockerComposeOnPath(stack, "pull", outputCallback);

                    combinedStdOuts +=
                        "[PULL STDOUT]\n" + pullStdOut + "\nExit code: " + pullExitCode;
                    combinedStdErrs +=
                        "[PULL STDERR]\n" + pullStdErr + "\nExit code: " + pullExitCode;

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

                    int attemptCount = 0;

                    while (attemptCount < MaxRollbackAttempts)
                    {
                        if (attemptCount > 0)
                        {
                            var delayMs = (int)Math.Pow(2, attemptCount) * 1000; // 2s, 4s, 8s
                            await Task.Delay(delayMs);
                        }

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

                        rollbackStdOut += $"\n[ROLLBACK SUCCESS]\n" + reupStdOut;
                        rollbackStdErr += $"\n[ROLLBACK SUCCESS]\n" + reupStdErr;

                        break;
                    }

                    if (attemptCount >= MaxRollbackAttempts)
                        rollbackFailed = true;

                    throw;
                }
            }

            var targetApp = await db
                .Containers.Include(x => x.NewerVersions)
                .FirstAsync(x => x.Id == app.Id);

            var removedVersions = targetApp.NewerVersions.RemoveAll(x =>
                targetVersion.Id == x.Id || targetVersion.VersionNumber.IsNewerThan(x.VersionNumber)
            );

            relatedApps = await db
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
                    targetVersion.Id == x.Id
                    || targetVersion.VersionNumber.IsNewerThan(x.VersionNumber)
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
                sideEffectUpdated = await db
                    .Containers.Include(x => x.NewerVersions)
                    .Where(c => appsWithSharedEnvVersion.Select(x => x.Id).Contains(c.Id))
                    .ToListAsync();

                foreach (var sideApp in sideEffectUpdated)
                {
                    sideApp.NewerVersions.RemoveAll(x =>
                        targetVersion.VersionNumber == x.VersionNumber
                        || targetVersion.VersionNumber.IsNewerThan(x.VersionNumber)
                    );
                    sideApp.Version = newVersion;
                }
            }

            CleanUpAllContainerJobs(app.Id, relatedApps, sideEffectUpdated);

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

            await _notificationService.SendAutoUpdateResult(
                app,
                targetVersion.VersionNumber,
                true,
                isAutomatic,
                false
            );

            _logger.LogInformation(
                "Updated through {UpdateCount} versions from {InitialVersion} to {NewVersion}.",
                removedVersions,
                app.Version,
                newVersion
            );

            return new(updateSteps);
        }
        catch (DockerCommandException ex)
        {
            // DockerCommandException bubbles up from inner try-catch after rollback is performed
            // This catch handles post-failure cleanup: UpdateAttempt logging, job cleanup, notifications
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var stack = await db.Stacks.FirstAsync(x => x.Id == app.StackId);

            if (rollbackFailed)
                rollbackStdErr += $"\n{LogAndGetRollbackFailedMessage()}";

            db.UpdateAttempts.Add(
                new()
                {
                    StdOut = ex.StdOut + rollbackStdOut,
                    StdErr = ex.StdErr + rollbackStdErr,
                    ExitCode = ex.ExitCode,
                    StartedAt = startedAt,
                    EndedAt = DateTime.UtcNow,
                    ContainerId = app.Id,
                    StackId = stack.Id,
                    FailedCommand = ex.Command,
                    UsedPlan = string.Join(", ", updateSteps),
                }
            );
            await db.SaveChangesAsync();

            CleanUpAllContainerJobs(app.Id, relatedApps, sideEffectUpdated);

            await _notificationService.SendAutoUpdateResult(
                app,
                targetVersion.VersionNumber,
                false,
                isAutomatic,
                rollbackFailed,
                ex.Message
            );

            throw;
        }
        catch (Exception ex)
        {
            if (!planOnly)
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync();
                var stack = await db.Stacks.FirstAsync(x => x.Id == app.StackId);

                db.UpdateAttempts.Add(
                    new()
                    {
                        StdOut = string.Empty,
                        StdErr =
                            ex
                            + (
                                rollbackFailed
                                    ? $"\n{LogAndGetRollbackFailedMessage()}"
                                    : string.Empty
                            ),
                        ExitCode = -1,
                        StartedAt = startedAt,
                        EndedAt = DateTime.UtcNow,
                        ContainerId = app.Id,
                        StackId = stack.Id,
                        FailedCommand = "Unhandled exception",
                        UsedPlan = string.Join(", ", updateSteps),
                    }
                );
                await db.SaveChangesAsync();

                CleanUpAllContainerJobs(app.Id, relatedApps, sideEffectUpdated);

                await _notificationService.SendAutoUpdateResult(
                    app,
                    targetVersion.VersionNumber,
                    false,
                    isAutomatic,
                    rollbackFailed,
                    ex.Message
                );
            }

            throw;
        }
    }
}
