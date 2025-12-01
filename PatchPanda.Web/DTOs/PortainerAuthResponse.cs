using System.Text.Json.Serialization;

namespace PatchPanda.Web.DTOs;

public class PortainerAuthResponse
{
    [JsonPropertyName("jwt")]
    public required string Jwt { get; set; }
}
