using System.Text.Json;

namespace PatchPanda.Web.Services.Background;

public class UpdateBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _serviceProvider;
    private readonly JobRegistry _jobRegistry;
    private readonly JobQueue _queue;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    public UpdateBackgroundService(
        IServiceScopeFactory serviceProvider,
        JobRegistry jobRegistry,
        JobQueue queue
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

    private async Task ProcessJob<TJob>(
        TJob job,
        ILogger<UpdateBackgroundService> logger,
        IServiceScope scope,
        Func<string, Task> function
    )
        where TJob : AbstractJob
    {
        if (!_jobRegistry.TryStartProcessing(job.Sequence))
            return;

        var jobName = job.GetType().Name;

        _jobRegistry.AppendOutput(job.Sequence, $"Starting job type {jobName} (queued)...");

        try
        {
            await function.Invoke(jobName);

            _jobRegistry.AppendOutput(job.Sequence, $"{jobName} finished.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error while running job {JobName}, full job: {Job}",
                jobName,
                JsonSerializer.Serialize(job)
            );
            _jobRegistry.AppendOutput(job.Sequence, $"{jobName} failed: " + ex.Message);
        }
        finally
        {
            _jobRegistry.FinishProcessing(job.Sequence);
        }
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
                    await ProcessJob(
                        updateJob,
                        logger,
                        scope,
                        async (string jobName) =>
                        {
                            var updateService =
                                scope.ServiceProvider.GetRequiredService<UpdateService>();
                            var dbFactory = scope.ServiceProvider.GetRequiredService<
                                IDbContextFactory<DataContext>
                            >();

                            await using var db = await dbFactory.CreateDbContextAsync(
                                cancellationToken
                            );
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
                                _jobRegistry.AppendOutput(
                                    updateJob.Sequence,
                                    "Container not found."
                                );
                                _jobRegistry.FinishProcessing(updateJob.Sequence);
                                return;
                            }

                            var targetVersion = app.NewerVersions.FirstOrDefault(v =>
                                v.Id == updateJob.TargetVersionId
                            );

                            if (targetVersion is null)
                                return;

                            await updateService.Update(
                                app,
                                false,
                                targetVersion,
                                (line) => _jobRegistry.AppendOutput(updateJob.Sequence, line),
                                updateJob.IsAutomatic
                            );
                        }
                    );
                    break;

                case ResetAllJob resetAllJob:
                    await ProcessJob(
                        resetAllJob,
                        logger,
                        scope,
                        async (string jobName) =>
                        {
                            var dockerService =
                                scope.ServiceProvider.GetRequiredService<DockerService>();
                            await dockerService.ResetComposeStacks();
                        }
                    );
                    break;

                case CheckForUpdatesAllJob checkForUpdatesAllJob:
                    await ProcessJob(
                        checkForUpdatesAllJob,
                        logger,
                        scope,
                        async (string jobName) =>
                        {
                            var updateService =
                                scope.ServiceProvider.GetRequiredService<UpdateService>();
                            await updateService.CheckAllForUpdates();
                        }
                    );
                    break;

                case RestartStackJob restartStackJob:
                    await ProcessJob(
                        restartStackJob,
                        logger,
                        scope,
                        async (string jobName) =>
                        {
                            var dockerService =
                                scope.ServiceProvider.GetRequiredService<DockerService>();
                            var dbFactory = scope.ServiceProvider.GetRequiredService<
                                IDbContextFactory<DataContext>
                            >();

                            using var db = dbFactory.CreateDbContext();
                            var stack = await db
                                .Stacks.Include(x => x.Apps)
                                .FirstOrDefaultAsync(
                                    x => x.Id == restartStackJob.StackId,
                                    cancellationToken
                                );

                            if (stack is null)
                            {
                                logger.LogInformation(
                                    "Disregarding job for missing stack {StackId}",
                                    restartStackJob.StackId
                                );
                                _jobRegistry.AppendOutput(
                                    restartStackJob.Sequence,
                                    "Stack not found."
                                );
                                _jobRegistry.FinishProcessing(restartStackJob.Sequence);
                                return;
                            }

                            await dockerService.RunDockerComposeOnPath(
                                stack,
                                "restart",
                                (line) => _jobRegistry.AppendOutput(restartStackJob.Sequence, line)
                            );
                        }
                    );
                    break;

                default:
                    logger.LogError("Disregarding unknown job type {JobType}", job.GetType().Name);
                    break;
            }
        }
    }
}
