using System.ComponentModel.DataAnnotations;

namespace PatchPanda.Web.Entities;

public abstract class AbstractEntity
{
    [Key]
    public int Id { get; set; } = 0;
}
