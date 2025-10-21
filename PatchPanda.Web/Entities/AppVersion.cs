namespace PatchPanda.Web.Entities;

public class AppVersion : AbstractEntity
{
    public required string VersionNumber { get; set; }

    public required bool Prerelease { get; set; }

    public required bool Breaking { get; set; }

    public required string Name { get; set; }

    public required string Body { get; set; }

    public bool Notified { get; set; }

    public virtual List<Container> Applications { get; set; } = [];
}
