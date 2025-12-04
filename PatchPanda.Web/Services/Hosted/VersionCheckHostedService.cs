namespace PatchPanda.Web.Services.Hosted;

public class VersionCheckHostedService : IHostedService
{
    private readonly ILogger<VersionCheckHostedService> _logger;
    private readonly JobRegistry _jobRegistry;

    private Timer? _timer;

    public VersionCheckHostedService(
        ILogger<VersionCheckHostedService> logger,
        JobRegistry jobRegistry
    )
    {
        ArgumentNullException.ThrowIfNull(jobRegistry);
        ArgumentNullException.ThrowIfNull(logger);

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
        await _jobRegistry.MarkForResetAll();
        await _jobRegistry.MarkForCheckUpdatesAll();
    }
}
