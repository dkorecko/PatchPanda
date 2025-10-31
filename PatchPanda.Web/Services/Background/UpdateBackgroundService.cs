namespace PatchPanda.Web.Services.Background;

public class UpdateBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _serviceProvider;
    private readonly UpdateRegistry _registry;
    private readonly UpdateQueue _queue;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    public UpdateBackgroundService(
        IServiceScopeFactory serviceProvider,
        UpdateRegistry registry,
        UpdateQueue queue
    )
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _queue = queue;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _processingTask?.GetAwaiter().GetResult();
        _cts?.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<UpdateBackgroundService>>();

        logger.LogInformation("Update background service starting (queue consumer)");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = Task.Run(() => ProcessQueueAsync(_cts.Token));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetService<ILogger<UpdateBackgroundService>>();

        logger?.LogInformation("Update background service stopping");

        _cts?.Cancel();

        try
        {
            _processingTask?.Wait(cancellationToken);
        }
        catch (OperationCanceledException) { }

        return Task.CompletedTask;
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!_registry.TryStartProcessing(work.ContainerId))
                continue;

            _registry.AppendOutput(work.ContainerId, "Starting update (queued)...");
            using var scope = _serviceProvider.CreateScope();

            try
            {
                var updateService = scope.ServiceProvider.GetRequiredService<UpdateService>();
                var dbFactory = scope.ServiceProvider.GetRequiredService<
                    IDbContextFactory<DataContext>
                >();

                using var db = dbFactory.CreateDbContext();
                var app = await db
                    .Containers.Include(x => x.NewerVersions)
                    .FirstAsync(x => x.Id == work.ContainerId, cancellationToken);

                await updateService.Update(
                    app,
                    false,
                    (line) => _registry.AppendOutput(work.ContainerId, line),
                    app.NewerVersions.FirstOrDefault(v => v.Id == work.TargetVersionId)
                );

                _registry.AppendOutput(work.ContainerId, "Update finished.");
            }
            catch (Exception ex)
            {
                var logger = scope.ServiceProvider.GetRequiredService<
                    ILogger<UpdateBackgroundService>
                >();
                logger.LogError(
                    ex,
                    "Error while updating container {ContainerId}",
                    work.ContainerId
                );
                _registry.AppendOutput(work.ContainerId, "Update failed: " + ex.Message);
            }
            finally
            {
                _registry.FinishProcessing(work.ContainerId);
            }
        }
    }
}
