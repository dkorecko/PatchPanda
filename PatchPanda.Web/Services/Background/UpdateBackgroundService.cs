using System.Text.Json;

namespace PatchPanda.Web.Services.Background;

public class UpdateBackgroundService(
    IServiceScopeFactory serviceProvider,
    JobRegistry jobRegistry,
    JobQueue queue
) : IHostedService, IDisposable
{
    private const int JobTimeoutSeconds = Constants.Limits.UPDATE_JOB_TIMEOUT_SECONDS;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    public void Dispose()
    {
        _cts?.Cancel();
        _processingTask?.GetAwaiter().GetResult();
        _cts?.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<UpdateBackgroundService>>();

        logger.LogInformation("Update background service starting (queue consumer)");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = Task.Run(() => ProcessQueueAsync(_cts.Token));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
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
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task> function
    )
        where TJob : AbstractJob
    {
        if (!jobRegistry.TryStartProcessing(job.Sequence))
            return;

        var jobName = job.GetType().Name;

        jobRegistry.AppendOutput(job.Sequence, $"Starting job type {jobName} (queued)...");

        using var jobTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        jobTimeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, JobTimeoutSeconds)));

        try
        {
            await function.Invoke(jobName, jobTimeoutCts.Token);

            jobRegistry.AppendOutput(job.Sequence, $"{jobName} finished.");
        }
        catch (OperationCanceledException ex) when (jobTimeoutCts.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Job {JobName} timed out or was cancelled after {TimeoutSeconds} seconds",
                jobName,
                JobTimeoutSeconds
            );
            jobRegistry.AppendOutput(
                job.Sequence,
                $"{jobName} timed out or was cancelled after {JobTimeoutSeconds} seconds."
            );
        }
        catch (TimeoutException ex)
        {
            logger.LogError(ex, "Job {JobName} timed out", jobName);
            jobRegistry.AppendOutput(job.Sequence, $"{jobName} timed out: " + ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error while running job {JobName}, full job: {Job}",
                jobName,
                JsonSerializer.Serialize(job)
            );
            jobRegistry.AppendOutput(job.Sequence, $"{jobName} failed: " + ex.Message);
        }
        finally
        {
            jobRegistry.FinishProcessing(job.Sequence);
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<
                ILogger<UpdateBackgroundService>
            >();

            switch (job)
            {
                case UpdateJob updateJob:
                    await ProcessJob(
                        updateJob,
                        logger,
                        cancellationToken,
                        async (string jobName, CancellationToken jobCancellationToken) =>
                        {
                            var updateService =
                                scope.ServiceProvider.GetRequiredService<UpdateService>();
                            var dbFactory = scope.ServiceProvider.GetRequiredService<
                                IDbContextFactory<DataContext>
                            >();

                            await using var db = await dbFactory.CreateDbContextAsync(
                                jobCancellationToken
                            );
                            var app = await db
                                .Containers.Include(x => x.NewerVersions)
                                .FirstOrDefaultAsync(
                                    x => x.Id == updateJob.ContainerId,
                                    jobCancellationToken
                                );

                            if (app is null)
                            {
                                logger.LogInformation(
                                    "Disregarding job for missing container {ContainerId}",
                                    updateJob.ContainerId
                                );
                                jobRegistry.AppendOutput(
                                    updateJob.Sequence,
                                    "Container not found."
                                );
                                jobRegistry.FinishProcessing(updateJob.Sequence);
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
                                (line) => jobRegistry.AppendOutput(updateJob.Sequence, line),
                                updateJob.IsAutomatic,
                                jobCancellationToken
                            );
                        }
                    );
                    break;

                case ResetAllJob resetAllJob:
                    await ProcessJob(
                        resetAllJob,
                        logger,
                        cancellationToken,
                        async (string jobName, CancellationToken jobCancellationToken) =>
                        {
                            var dockerService =
                                scope.ServiceProvider.GetRequiredService<DockerService>();
                            await dockerService.ResetComposeStacks(jobCancellationToken);
                        }
                    );
                    break;

                case CheckForUpdatesAllJob checkForUpdatesAllJob:
                    await ProcessJob(
                        checkForUpdatesAllJob,
                        logger,
                        cancellationToken,
                        async (string jobName, CancellationToken jobCancellationToken) =>
                        {
                            var updateService =
                                scope.ServiceProvider.GetRequiredService<UpdateService>();
                            await updateService.CheckAllForUpdates(jobCancellationToken);
                        }
                    );
                    break;

                case RestartStackJob restartStackJob:
                    await ProcessJob(
                        restartStackJob,
                        logger,
                        cancellationToken,
                        async (string jobName, CancellationToken jobCancellationToken) =>
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
                                    jobCancellationToken
                                );

                            if (stack is null)
                            {
                                logger.LogInformation(
                                    "Disregarding job for missing stack {StackId}",
                                    restartStackJob.StackId
                                );
                                jobRegistry.AppendOutput(
                                    restartStackJob.Sequence,
                                    "Stack not found."
                                );
                                jobRegistry.FinishProcessing(restartStackJob.Sequence);
                                return;
                            }

                            await dockerService.RunDockerComposeOnPath(
                                stack,
                                "restart",
                                (line) => jobRegistry.AppendOutput(restartStackJob.Sequence, line),
                                jobCancellationToken
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
