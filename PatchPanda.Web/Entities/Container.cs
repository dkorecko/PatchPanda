namespace PatchPanda.Web.Entities;

public class Container : AbstractEntity
{
    public required string Name { get; set; }

    public required string? Version { get; set; }

    public required string CurrentSha { get; set; }

    public Tuple<string, string>? GitHubRepo { get; set; }

    public List<Tuple<string, string>>? SecondaryGitHubRepos { get; set; }

    public required string Uptime { get; set; }

    public required string TargetImage { get; set; }

    public string? Regex { get; set; }

    public string? GitHubVersionRegex { get; set; }

    public int? MultiContainerAppId { get; set; }

    public virtual MultiContainerApp? MultiContainerApp { get; set; }

    public bool IsSecondary { get; set; }

    public DateTime LastVersionCheck { get; set; } = DateTime.MinValue;

    public virtual List<AppVersion> NewerVersions { get; set; } = [];

    public required int StackId { get; set; }

    public virtual ComposeStack Stack { get; set; } = null!;
}
