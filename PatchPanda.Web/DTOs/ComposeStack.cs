namespace PatchPanda.Web.DTOs;

public class ComposeStack
{
    public required string StackName { get; set; }

    public required string ConfigFile { get; set; }

    public List<ComposeApp> Apps { get; set; } = [];

    public HashSet<string> MultiContainerApps { get; set; } = [];
}
