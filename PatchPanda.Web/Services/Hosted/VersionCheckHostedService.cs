namespace PatchPanda.Web.Services.Hosted;

public class VersionCheckHostedService : IHostedService
{
    private readonly DockerService _dockerService;
    private readonly VersionService _versionService;
    private readonly DiscordService _discordService;
    private readonly IDbContextFactory<DataContext> _dbContextFactory;
    private readonly ILogger<VersionCheckHostedService> _logger;

    private Timer? _timer;
    private volatile bool _pushedOnce;

    public VersionCheckHostedService(
        DockerService dockerService,
        VersionService versionService,
        DiscordService discordService,
        IDbContextFactory<DataContext> dbContextFactory,
        ILogger<VersionCheckHostedService> logger
        )
    {
        ArgumentNullException.ThrowIfNull(dockerService);
        ArgumentNullException.ThrowIfNull(versionService);
        ArgumentNullException.ThrowIfNull(discordService);
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _dockerService = dockerService;
        _versionService = versionService;
        _discordService = discordService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
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

            var isReset = await _dockerService.ResetComposeStacks();

            if (!isReset)
            {
                return;
            }

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
                        await _discordService.SendUpdates(mainApp, [.. otherApps]);
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
