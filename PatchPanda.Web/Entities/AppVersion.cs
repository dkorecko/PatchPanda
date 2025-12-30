namespace PatchPanda.Web.Entities;

public class AppVersion : AbstractEntity
{
    public required string VersionNumber { get; set; }

    public required bool Prerelease { get; set; }

    public required bool Breaking { get; set; }

    public required string Name { get; set; }

    public required string Body { get; set; }

    public string? AISummary { get; set; }

    public bool? AIBreaking { get; set; }

    public bool Notified { get; set; }

    public bool Ignored { get; set; }

    public DateTime DateDiscovered { get; set; } = DateTime.Now;

    public virtual List<Container> Applications { get; set; } = [];

    public string? SecurityAnalysis { get; set; }

    public bool? IsSuspectedMalicious { get; set; }
}
