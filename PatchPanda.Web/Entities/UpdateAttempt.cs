namespace PatchPanda.Web.Entities;

public class UpdateAttempt : AbstractEntity
{
    public required int ContainerId { get; set; }
    public required int StackId { get; set; }
    public required DateTime StartedAt { get; set; }
    public required DateTime EndedAt { get; set; } // does not matter if failure or success
    public required string? StdOut { get; set; }
    public required string? StdErr { get; set; }
    public string? FailedCommand { get; set; }
    public int? ExitCode { get; set; } = 0;
    public required string UsedPlan { get; set; }

    public bool IsFailed => !string.IsNullOrEmpty(FailedCommand);
}
