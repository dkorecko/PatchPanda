namespace PatchPanda.Web.DTOs;

public class ComposeApp
{
    public required string Name { get; set; }

    public required string? Version { get; set; }

    public IEnumerable<AppVersion> NewerVersions { get; set; } = [];

    public required string CurrentSha { get; set; }

    public required string? GitHubRepo { get; set; }

    public required string Uptime { get; set; }

    public required string TargetImage { get; set; }

    public required string? Regex { get; set; }

    public string? FromMultiContainer { get; set; }

    public bool IsSecondary { get; set; }
}
