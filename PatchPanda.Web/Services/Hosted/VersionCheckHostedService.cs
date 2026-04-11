namespace PatchPanda.Web.Services.Hosted;

public class VersionCheckHostedService : IHostedService, IDisposable
{
    private readonly ILogger<VersionCheckHostedService> _logger;
    private readonly JobRegistry _jobRegistry;

    private Timer? _timer;
    private int _isRunning;

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

    private async void DoWork(object? state)
    {
        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
        {
            _logger.LogInformation(
                "Skipping scheduled version check because a previous run is still enqueueing jobs."
            );
            return;
        }

        try
        {
            var queuedReset = await _jobRegistry.TryMarkForResetAll();
            if (!queuedReset)
                _logger.LogInformation(
                    "Skipping ResetAll enqueue because one is already queued or processing."
                );

            var queuedCheck = await _jobRegistry.TryMarkForCheckUpdatesAll();
            if (!queuedCheck)
                _logger.LogInformation(
                    "Skipping CheckForUpdatesAll enqueue because one is already queued or processing."
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while enqueueing scheduled version check jobs");
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }
}
