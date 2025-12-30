namespace PatchPanda.Web.Entities;

public class AppSetting
{
    [Key]
    public required string Key { get; init; }

    public required string Value { get; set; }
}
