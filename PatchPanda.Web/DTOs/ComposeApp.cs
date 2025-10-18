namespace PatchPanda.Web.DTOs;

public class ComposeApp
{
    public required string Name { get; set; }

    public required string Version { get; set; }

    public required string CurrentSha { get; set; }

    public required string GitHubRepo { get; set; }

    public required string Uptime { get; set; }

    public required string Regex { get; set; }
}
