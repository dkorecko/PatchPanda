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
        var uniqueApps = currentData.SelectMany(x => x.Apps).DistinctBy(x => x.Name);

        foreach (var uniqueApp in uniqueApps.OrderBy(x => x.NewerVersions.Count()))
        {
            try
            {
                List<AppVersion> newerVersions;

                newerVersions = (await versionService.GetNewerVersions(uniqueApp)).ToList();

                if (newerVersions.Any())
                {
                    var discordService = scope.ServiceProvider.GetRequiredService<DiscordService>();
                    await discordService.SendUpdates(uniqueApp);
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
            }
        }
    }
}
