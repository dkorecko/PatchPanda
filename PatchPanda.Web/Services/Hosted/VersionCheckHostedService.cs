namespace PatchPanda.Web.Services.Hosted;

public class VersionCheckHostedService : IHostedService
{
    private readonly IVersionService _versionService;
    private readonly DiscordService _discordService;
    private readonly AppriseService _appriseService;
    private readonly IDbContextFactory<DataContext> _dbContextFactory;
    private readonly ILogger<VersionCheckHostedService> _logger;
    private readonly JobRegistry _jobRegistry;

    private Timer? _timer;
    private volatile bool _pushedOnce;

    public VersionCheckHostedService(
        IVersionService versionService,
        DiscordService discordService,
        AppriseService appriseService,
        IDbContextFactory<DataContext> dbContextFactory,
        ILogger<VersionCheckHostedService> logger,
        JobRegistry jobRegistry
    )
    {
        ArgumentNullException.ThrowIfNull(versionService);
        ArgumentNullException.ThrowIfNull(discordService);
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(jobRegistry);

        _versionService = versionService;
        _discordService = discordService;
        _appriseService = appriseService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _jobRegistry = jobRegistry;

        if (string.IsNullOrWhiteSpace(Constants.BASE_URL))
            _logger.LogWarning(
                "{BaseUrlKey} was not set, therefore update URLs will not be provided.",
                Constants.VariableKeys.BASE_URL
            );
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Service starting");

        DoWork(null);
        _timer = new Timer(DoWork, null, TimeSpan.FromHours(2), TimeSpan.FromHours(2));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Service stopping");

        _timer!.Change(Timeout.Infinite, 0);
        Dispose();

        return Task.CompletedTask;
    }

    public async void DoWork(object? state)
    {
        using var db = _dbContextFactory.CreateDbContext();

        var containers = await db
            .Containers.Include(x => x.NewerVersions)
            .Where(x => !x.IsSecondary && (x.GitHubRepo != null || x.OverrideGitHubRepo != null))
            .ToListAsync();

        if (containers.Count == 0)
        {
            if (_pushedOnce)
                return;

            await _jobRegistry.MarkForResetAll();

            _pushedOnce = true;
            DoWork(null);
            return;
        }

        _pushedOnce = false;

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
                    var otherApps = currentVersionGroup.Skip(1);

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
    }
}
