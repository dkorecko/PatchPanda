using System.Collections.Concurrent;

namespace PatchPanda.Web.Helpers;

public abstract class PendingUpdate
{
    public bool IsProcessing { get; set; }
    public long Sequence { get; set; }
    public List<string> Output { get; } = [];
    public abstract string Kind { get; }
}

public class PendingUpdateJob : PendingUpdate
{
    public required int ContainerId { get; set; }
    public required int TargetVersionId { get; set; }
    public required string TargetVersionNumber { get; set; }
    public override string Kind => "Update";
}

public class PendingResetAll : PendingUpdate
{
    public override string Kind => "ResetAll";
}

public class PendingRestartStack : PendingUpdate
{
    public required int StackId { get; set; }
    public override string Kind => "RestartStack";
}

public class PendingCheckForUpdatesAll : PendingUpdate
{
    public override string Kind => "CheckForUpdatesAll";
}

public class JobRegistry
{
    private readonly ConcurrentDictionary<long, PendingUpdate> _pending = new();
    private readonly JobQueue _updateQueue;
    private long _sequenceCounter;
    private readonly object _processingLock = new();

    public JobRegistry(JobQueue updateQueue)
    {
        _updateQueue = updateQueue;
    }

    private long GetNextSequence() => Interlocked.Increment(ref _sequenceCounter);

    public async Task MarkForUpdate(
        int containerId,
        int targetVersionId,
        string targetVersionNumber
    )
    {
        var seq = GetNextSequence();

        var pending = new PendingUpdateJob
        {
            ContainerId = containerId,
            TargetVersionId = targetVersionId,
            TargetVersionNumber = targetVersionNumber,
            Sequence = seq
        };

        _pending.TryAdd(seq, pending);

        await _updateQueue.EnqueueAsync(
            new UpdateJob(seq, containerId, targetVersionId, targetVersionNumber)
        );
    }

    public async Task MarkForResetAll()
    {
        var seq = GetNextSequence();

        var pending = new PendingResetAll { Sequence = seq };

        _pending.TryAdd(seq, pending);

        await _updateQueue.EnqueueAsync(new ResetAllJob(seq));
    }

    public async Task MarkForCheckUpdatesAll()
    {
        var seq = GetNextSequence();

        var pending = new PendingCheckForUpdatesAll { Sequence = seq };

        _pending.TryAdd(seq, pending);

        await _updateQueue.EnqueueAsync(new CheckForUpdatesAllJob(seq));
    }

    public async Task MarkForRestartStack(int stackId)
    {
        var seq = GetNextSequence();

        var pending = new PendingRestartStack { StackId = stackId, Sequence = seq };

        _pending.TryAdd(seq, pending);

        await _updateQueue.EnqueueAsync(new RestartStackJob(seq, stackId));
    }

    public bool TryStartProcessing(long sequence)
    {
        lock (_processingLock)
        {
            if (_pending.TryGetValue(sequence, out var pending) && !pending.IsProcessing)
            {
                pending.IsProcessing = true;
                return true;
            }
        }

        return false;
    }

    public void FinishProcessing(long sequence)
    {
        if (_pending.TryGetValue(sequence, out var pending))
        {
            pending.IsProcessing = false;
            _pending.TryRemove(sequence, out _);
        }
    }

    public void AppendOutput(long sequence, string line)
    {
        var pending = _pending.GetValueOrDefault(sequence);

        if (pending is null)
            return;

        lock (pending.Output)
        {
            pending.Output.Add(line);
        }
    }

    public List<string> GetOutputSnapshot(long sequence)
    {
        if (!_pending.TryGetValue(sequence, out var pending))
            return [];

        lock (pending.Output)
        {
            return [.. pending.Output];
        }
    }

    public long? GetQueuedUpdateForContainer(int containerId) =>
        _pending
            .Values.FirstOrDefault(p =>
                p is PendingUpdateJob u && u.ContainerId == containerId && !p.IsProcessing
            )
            ?.Sequence;

    public long? GetProcessingUpdateForContainer(int containerId) =>
        _pending
            .Values.FirstOrDefault(p =>
                p is PendingUpdateJob u && u.ContainerId == containerId && p.IsProcessing
            )
            ?.Sequence;

    public List<PendingUpdate> GetSnapshot()
    {
        var list = new List<PendingUpdate>();

        foreach (var kv in _pending)
        {
            var p = kv.Value;

            if (p is PendingUpdateJob uj)
            {
                var copy = new PendingUpdateJob
                {
                    ContainerId = uj.ContainerId,
                    TargetVersionId = uj.TargetVersionId,
                    TargetVersionNumber = uj.TargetVersionNumber,
                    IsProcessing = uj.IsProcessing,
                    Sequence = uj.Sequence
                };

                lock (uj.Output)
                {
                    copy.Output.AddRange(uj.Output);
                }

                list.Add(copy);
            }
            else if (p is PendingRestartStack rs)
            {
                var copy = new PendingRestartStack
                {
                    StackId = rs.StackId,
                    IsProcessing = rs.IsProcessing,
                    Sequence = rs.Sequence
                };

                lock (rs.Output)
                {
                    copy.Output.AddRange(rs.Output);
                }

                list.Add(copy);
            }
            else if (p is PendingResetAll ra)
            {
                var copy = new PendingResetAll
                {
                    IsProcessing = ra.IsProcessing,
                    Sequence = ra.Sequence
                };

                lock (ra.Output)
                {
                    copy.Output.AddRange(ra.Output);
                }

                list.Add(copy);
            }
        }

        return list.OrderBy(x => x.Sequence).ToList();
    }

    public bool IsQueuedResetAll() =>
        _pending.Values.Any(p => p is PendingResetAll && !p.IsProcessing);

    public bool IsProcessingResetAll() =>
        _pending.Values.Any(p => p is PendingResetAll && p.IsProcessing);

    public bool IsQueuedCheckUpdatesAll() =>
        _pending.Values.Any(p => p is PendingCheckForUpdatesAll && !p.IsProcessing);

    public bool IsProcessingCheckUpdatesAll() =>
        _pending.Values.Any(p => p is PendingCheckForUpdatesAll && p.IsProcessing);

    public bool IsQueuedRestartForStack(int stackId) =>
        _pending.Values.Any(p =>
            p is PendingRestartStack rs && rs.StackId == stackId && !p.IsProcessing
        );

    public bool IsProcessingRestartForStack(int stackId) =>
        _pending.Values.Any(p =>
            p is PendingRestartStack rs && rs.StackId == stackId && p.IsProcessing
        );

    public bool TryRemove(long sequence)
    {
        lock (_processingLock)
        {
            if (_pending.TryGetValue(sequence, out var pending) && !pending.IsProcessing)
            {
                return _pending.TryRemove(sequence, out _);
            }
        }

        return false;
    }
}
