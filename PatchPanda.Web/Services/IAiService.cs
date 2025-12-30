namespace PatchPanda.Web.Services;

public interface IAiService
{
    Task<SummaryResult?> SummarizeReleaseNotes(string releaseNotes);

    Task<SecurityAnalysisResult?> AnalyzeDiff(string diff);

    bool IsInitialized();
}

public interface IAiResult
{
    
}

public class SummaryResult : IAiResult
{
    public required string Summary { get; set; }

    public required bool Breaking { get; set; }
}

public class SecurityAnalysisResult : IAiResult
{
    public required string Analysis { get; set; }

    public required bool IsSuspectedMalicious { get; set; }
}
