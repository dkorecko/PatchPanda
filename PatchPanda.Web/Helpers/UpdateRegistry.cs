using System.Collections.Concurrent;

namespace PatchPanda.Web.Helpers;

public class PendingUpdate
{
    public required int ContainerId { get; set; }
    public required int TargetVersionId { get; set; }
    public required string TargetVersionNumber { get; set; }
    public bool IsProcessing { get; set; }
    public List<string> Output { get; } = [];
}

public class UpdateRegistry
{
    private readonly ConcurrentDictionary<int, PendingUpdate> _pending = new();

    public void MarkForUpdate(int containerId, int targetVersionId, string targetVersionNumber)
    {
        _pending.TryAdd(
            containerId,
            new PendingUpdate
            {
                ContainerId = containerId,
                TargetVersionId = targetVersionId,
                TargetVersionNumber = targetVersionNumber
            }
        );
    }

    public bool TryStartProcessing(int containerId)
    {
        if (!_pending.TryGetValue(containerId, out var pending))
            return false;

        if (pending.IsProcessing)
            return false;

        pending.IsProcessing = true;
        return true;
    }

    public void FinishProcessing(int containerId)
    {
        if (_pending.TryGetValue(containerId, out var pending))
        {
            pending.IsProcessing = false;
            _pending.TryRemove(containerId, out _);
        }
    }

    public void AppendOutput(int containerId, string line)
    {
        var pending = _pending.GetValueOrDefault(containerId);

        lock (pending!.Output)
        {
            pending.Output.Add(line);
        }
    }

    public List<string> GetOutputSnapshot(int containerId)
    {
        if (!_pending.TryGetValue(containerId, out var pending))
            return [];

        lock (pending.Output)
        {
            return [.. pending.Output];
        }
    }

    public bool IsMarked(int containerId) => _pending.ContainsKey(containerId);
}
