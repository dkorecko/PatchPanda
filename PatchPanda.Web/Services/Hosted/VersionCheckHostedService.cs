namespace PatchPanda.Web.Services.Hosted;

public class VersionCheckHostedService : IHostedService
{
    private readonly IServiceScopeFactory _serviceProvider;
    private Timer? _timer;

    public VersionCheckHostedService(IServiceScopeFactory serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Dispose()
    {
        _timer!.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<VersionCheckHostedService>>();

        logger.LogInformation("Service starting");

        DoWork(null);
        _timer = new Timer(DoWork, null, TimeSpan.FromHours(2), TimeSpan.FromHours(2));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetService<ILogger<VersionCheckHostedService>>();

        logger!.LogInformation("Service stopping");

        _timer!.Change(Timeout.Infinite, 0);
        Dispose();

        return Task.CompletedTask;
    }

    public async void DoWork(object? state)
    {
        using var scope = _serviceProvider.CreateScope();
        var versionService = scope.ServiceProvider.GetRequiredService<VersionService>();
        var dataService = scope.ServiceProvider.GetRequiredService<DataService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<VersionCheckHostedService>>();

        var currentData = await dataService.GetData();
        var uniqueRepoGroups = currentData
            .SelectMany(x => x.Apps)
            .DistinctBy(x => x.Name)
            .Where(x => !x.IsSecondary)
            .GroupBy(x => x.GitHubRepo)
            .Where(x => x.Key is not null)
            .OrderBy(x => x.First().LastVersionCheck);

        foreach (var uniqueRepoGroup in uniqueRepoGroups)
        {
            logger.LogInformation("Checking unique repo group: {Repo}", uniqueRepoGroup.Key);

            var currentVersionGroups = uniqueRepoGroup
                .GroupBy(x => x.Version)
                .Where(x => x.Key is not null);

            foreach (var currentVersionGroup in currentVersionGroups)
            {
                logger.LogInformation(
                    "Checking version group: {Repo} {Version}",
                    uniqueRepoGroup.Key,
                    currentVersionGroup.Key
                );
                logger.LogInformation("Got {Count} containers", currentVersionGroup.Count());

                var mainApp = currentVersionGroup.First();

                logger.LogInformation(
                    "Selected {MainApp} as main application for getting releases",
                    mainApp.Name
                );
                try
                {
                    List<AppVersion> newerVersions;

                    newerVersions = (await versionService.GetNewerVersions(mainApp)).ToList();

                    if (newerVersions.Any())
                    {
                        var discordService =
                            scope.ServiceProvider.GetRequiredService<DiscordService>();
                        currentVersionGroup
                            .Skip(1)
                            .ToList()
                            .ForEach(app => versionService.SetNewerVersions(app, newerVersions));
                        await discordService.SendUpdates(
                            mainApp,
                            [.. currentVersionGroup.Skip(1).Select(x => x.Name)]
                        );
                    }

                    currentVersionGroup
                        .ToList()
                        .ForEach(app => app.LastVersionCheck = DateTime.Now);

                    await Task.Delay(5000);
                }
                catch (RateLimitException ex)
                {
                    logger.LogWarning(
                        "Rate limit {Limit} hit when checking for updates, skipping further checks until {Reset}",
                        ex.Limit,
                        ex.ResetsAt
                    );
                    return;
                }
            }
        }
    }
}
