namespace PatchPanda.Web.Entities;

public class AppSetting
{
    [Key]
    [MaxLength(64)]
    public required string Key { get; init; }

    [MaxLength(64)]
    public required string Value { get; set; }
}
