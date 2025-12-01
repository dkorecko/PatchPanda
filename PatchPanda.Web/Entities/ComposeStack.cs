namespace PatchPanda.Web.Entities;

public class ComposeStack : AbstractEntity
{
    public required string StackName { get; set; }

    public required string? ConfigFile { get; set; }

    public bool PortainerManaged { get; set; }

    public virtual List<Container> Apps { get; set; } = [];
}
