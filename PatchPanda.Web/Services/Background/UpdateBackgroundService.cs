namespace PatchPanda.Web.Services.Background;

public class UpdateBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _serviceProvider;
    private readonly JobRegistry _jobRegistry;
    private readonly UpdateQueue _queue;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    public UpdateBackgroundService(
        IServiceScopeFactory serviceProvider,
        JobRegistry jobRegistry,
        UpdateQueue queue
    )
    {
        _serviceProvider = serviceProvider;
        _jobRegistry = jobRegistry;
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
        await foreach (var job in _queue.Reader.ReadAllAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            using var scope = _serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<
                ILogger<UpdateBackgroundService>
            >();

            switch (job)
            {
                case UpdateJob updateJob:
                    if (!_jobRegistry.TryStartProcessing(updateJob.Sequence))
                        continue;

                    _jobRegistry.AppendOutput(updateJob.Sequence, "Starting update (queued)...");

                    try
                    {
                        var updateService =
                            scope.ServiceProvider.GetRequiredService<UpdateService>();
                        var dbFactory = scope.ServiceProvider.GetRequiredService<
                            IDbContextFactory<DataContext>
                        >();

                        using var db = dbFactory.CreateDbContext();
                        var app = await db
                            .Containers.Include(x => x.NewerVersions)
                            .FirstOrDefaultAsync(
                                x => x.Id == updateJob.ContainerId,
                                cancellationToken
                            );

                        if (app is null)
                        {
                            logger.LogInformation(
                                "Disregarding job for missing container {ContainerId}",
                                updateJob.ContainerId
                            );
                            _jobRegistry.AppendOutput(updateJob.Sequence, "Container not found.");
                            _jobRegistry.FinishProcessing(updateJob.Sequence);
                            continue;
                        }

                        await updateService.Update(
                            app,
                            false,
                            (line) => _jobRegistry.AppendOutput(updateJob.Sequence, line),
                            app.NewerVersions.FirstOrDefault(v => v.Id == updateJob.TargetVersionId)
                        );

                        _jobRegistry.AppendOutput(updateJob.Sequence, "Update finished.");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Error while updating container {ContainerId}",
                            updateJob.ContainerId
                        );
                        _jobRegistry.AppendOutput(
                            updateJob.Sequence,
                            "Update failed: " + ex.Message
                        );
                    }
                    finally
                    {
                        _jobRegistry.FinishProcessing(updateJob.Sequence);
                    }

                    break;

                case ResetAllJob rall:
                    if (!_jobRegistry.TryStartProcessing(rall.Sequence))
                        continue;

                    _jobRegistry.AppendOutput(
                        rall.Sequence,
                        "Starting full container reset (queued)..."
                    );

                    try
                    {
                        var dockerService =
                            scope.ServiceProvider.GetRequiredService<DockerService>();
                        var success = await dockerService.ResetComposeStacks();
                        _jobRegistry.AppendOutput(
                            rall.Sequence,
                            success ? "Reset finished." : "Reset failed."
                        );
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error while resetting all containers");
                        _jobRegistry.AppendOutput(rall.Sequence, "Reset failed: " + ex.Message);
                    }
                    finally
                    {
                        _jobRegistry.FinishProcessing(rall.Sequence);
                    }

                    break;

                case RestartStackJob rs:
                    if (!_jobRegistry.TryStartProcessing(rs.Sequence))
                        continue;

                    _jobRegistry.AppendOutput(rs.Sequence, "Starting stack restart (queued)...");

                    try
                    {
                        var dockerService =
                            scope.ServiceProvider.GetRequiredService<DockerService>();
                        var dbFactory = scope.ServiceProvider.GetRequiredService<
                            IDbContextFactory<DataContext>
                        >();

                        using var db = dbFactory.CreateDbContext();
                        var stack = await db
                            .Stacks.Include(x => x.Apps)
                            .FirstOrDefaultAsync(x => x.Id == rs.StackId, cancellationToken);

                        if (stack is null)
                        {
                            logger.LogInformation(
                                "Disregarding job for missing stack {StackId}",
                                rs.StackId
                            );
                            _jobRegistry.AppendOutput(rs.Sequence, "Stack not found.");
                            _jobRegistry.FinishProcessing(rs.Sequence);
                            continue;
                        }

                        await dockerService.RunDockerComposeOnPath(
                            stack,
                            "restart",
                            (line) => _jobRegistry.AppendOutput(rs.Sequence, line)
                        );

                        _jobRegistry.AppendOutput(rs.Sequence, "Restart finished.");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error while restarting stack {StackId}", rs.StackId);
                        _jobRegistry.AppendOutput(rs.Sequence, "Restart failed: " + ex.Message);
                    }
                    finally
                    {
                        _jobRegistry.FinishProcessing(rs.Sequence);
                    }

                    break;

                default:
                    logger.LogError("Disregarding unknown job type {JobType}", job.GetType().Name);
                    break;
            }
        }
    }
}
