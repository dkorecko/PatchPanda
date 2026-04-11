namespace PatchPanda.Units.Helpers;

public class JobRegistryTests
{
    [Fact]
    public async Task TryMarkForResetAll_DedupesQueuedAndProcessingJobs()
    {
        var registry = new JobRegistry(new JobQueue());

        var firstQueued = await registry.TryMarkForResetAll();
        var secondQueued = await registry.TryMarkForResetAll();

        Assert.True(firstQueued);
        Assert.False(secondQueued);

        var resetJob = Assert.Single(registry.GetSnapshot().OfType<PendingResetAll>());

        Assert.True(registry.TryStartProcessing(resetJob.Sequence));
        Assert.False(await registry.TryMarkForResetAll());

        registry.FinishProcessing(resetJob.Sequence);

        Assert.True(await registry.TryMarkForResetAll());
    }

    [Fact]
    public async Task TryMarkForCheckUpdatesAll_DedupesQueuedAndProcessingJobs()
    {
        var registry = new JobRegistry(new JobQueue());

        var firstQueued = await registry.TryMarkForCheckUpdatesAll();
        var secondQueued = await registry.TryMarkForCheckUpdatesAll();

        Assert.True(firstQueued);
        Assert.False(secondQueued);

        var checkJob = Assert.Single(registry.GetSnapshot().OfType<PendingCheckForUpdatesAll>());

        Assert.True(registry.TryStartProcessing(checkJob.Sequence));
        Assert.False(await registry.TryMarkForCheckUpdatesAll());

        registry.FinishProcessing(checkJob.Sequence);

        Assert.True(await registry.TryMarkForCheckUpdatesAll());
    }

    [Fact]
    public async Task TryMarkForRestartStack_DedupesPerStack()
    {
        var registry = new JobRegistry(new JobQueue());

        var firstQueued = await registry.TryMarkForRestartStack(12);
        var duplicateQueued = await registry.TryMarkForRestartStack(12);
        var differentStackQueued = await registry.TryMarkForRestartStack(24);

        Assert.True(firstQueued);
        Assert.False(duplicateQueued);
        Assert.True(differentStackQueued);

        var restartJob = Assert.Single(
            registry.GetSnapshot().OfType<PendingRestartStack>(),
            x => x.StackId == 12
        );

        Assert.True(registry.TryStartProcessing(restartJob.Sequence));
        Assert.False(await registry.TryMarkForRestartStack(12));

        registry.FinishProcessing(restartJob.Sequence);

        Assert.True(await registry.TryMarkForRestartStack(12));
    }
}
