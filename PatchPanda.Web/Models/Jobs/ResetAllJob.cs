namespace PatchPanda.Web.Models.Jobs;

public record ResetAllJob(long Sequence) : AbstractJob(Sequence);
