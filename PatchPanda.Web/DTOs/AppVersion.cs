namespace PatchPanda.Web.DTOs;

public class AppVersion
{
    public required string VersionNumber { get; set; }

    public required bool Prerelease { get; set; }

    public required string Name { get; set; }

    public required string Body { get; set; }
}
