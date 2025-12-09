namespace PatchPanda.Web.Services;

public interface IAIService
{
    Task<AIResult?> SummarizeReleaseNotes(string releaseNotes);

    bool IsInitialized();
}

public class AIResult
{
    public required string Summary { get; set; }

    public required bool Breaking { get; set; }
}
