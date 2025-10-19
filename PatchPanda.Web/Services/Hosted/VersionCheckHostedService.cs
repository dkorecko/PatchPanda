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

        var currentData = await dataService.GetData();
        var uniqueRepoGroups = currentData
            .SelectMany(x => x.Apps)
            .DistinctBy(x => x.Name)
            .Where(x => !x.IsSecondary)
            .GroupBy(x => x.GitHubRepo)
            .OrderBy(x => x.First().NewerVersions.Count());

        foreach (var uniqueRepoGroup in uniqueRepoGroups)
        {
            var currentVersionGroups = uniqueRepoGroup.GroupBy(x => x.Version);

            foreach (var currentVersionGroup in currentVersionGroups)
            {
                var mainApp = currentVersionGroup.First();
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

                    await Task.Delay(5000);
                }
                catch (RateLimitException)
                {
                    var logger = scope.ServiceProvider.GetRequiredService<
                        ILogger<VersionCheckHostedService>
                    >();
                    logger.LogWarning(
                        "Rate limit hit when checking for updates, skipping further checks"
                    );
                    return;
                }
            }
        }
    }
}
