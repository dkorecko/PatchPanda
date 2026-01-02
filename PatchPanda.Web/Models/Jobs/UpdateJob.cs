namespace PatchPanda.Web.Models.Jobs;

public record UpdateJob(
    long Sequence,
    int ContainerId,
    int TargetVersionId,
    string TargetVersionNumber,
    bool IsAutomatic = false
) : AbstractJob(Sequence);
