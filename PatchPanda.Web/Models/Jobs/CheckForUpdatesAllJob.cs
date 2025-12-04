namespace PatchPanda.Web.Models.Jobs;

public record CheckForUpdatesAllJob(long Sequence) : AbstractJob(Sequence);
