namespace PatchPanda.Web.Entities;

public class MultiContainerApp : AbstractEntity
{
    public required string AppName { get; set; }

    public virtual List<Container> Containers { get; set; } = [];
}
