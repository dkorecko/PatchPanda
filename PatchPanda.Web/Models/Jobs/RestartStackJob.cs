namespace PatchPanda.Web.Models.Jobs;

public record RestartStackJob(long Sequence, int StackId) : AbstractJob(Sequence);
