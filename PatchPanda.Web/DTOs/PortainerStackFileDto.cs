using System.Text.Json.Serialization;

namespace PatchPanda.Web.DTOs;

public class PortainerStackFileDto
{
    [JsonPropertyName("StackFileContent")]
    public required string StackFileContent { get; set; }
}
