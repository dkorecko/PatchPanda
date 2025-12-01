using System.Text.Json.Serialization;

namespace PatchPanda.Web.DTOs;

public class PortainerStackDto
{
    [JsonPropertyName("Id")]
    public required int Id { get; set; }

    [JsonPropertyName("Name")]
    public required string Name { get; set; }

    [JsonPropertyName("Type")]
    public int Type { get; set; }

    [JsonPropertyName("EndpointId")]
    public int EndpointId { get; set; }

    [JsonPropertyName("EntryPoint")]
    public string? EntryPoint { get; set; }

    [JsonPropertyName("ProjectPath")]
    public string? ProjectPath { get; set; }
}
