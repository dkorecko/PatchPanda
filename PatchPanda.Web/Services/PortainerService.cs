using System.Text;
using System.Text.Json;
using PatchPanda.Web.DTOs;

namespace PatchPanda.Web.Services;

public class PortainerService : IPortainerService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PortainerService> _logger;
    private readonly string? _url;
    private readonly string? _accessToken;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_url)
        && !string.IsNullOrWhiteSpace(_accessToken);

    public PortainerService(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<PortainerService> logger
    )
    {
        _logger = logger;
        _url = configuration.GetValue<string?>(Constants.VariableKeys.PORTAINER_URL);
        _accessToken = configuration.GetValue<string?>(Constants.VariableKeys.PORTAINER_ACCESS_TOKEN);
        _httpClient = httpFactory.CreateClient();

        if (!IsConfigured)
        {
            logger.LogWarning(
                "{Url} or {AccessToken} is missing. Please set both if you wish to enable Portainer integration.",
                Constants.VariableKeys.PORTAINER_URL,
                Constants.VariableKeys.PORTAINER_ACCESS_TOKEN
            );
        }
        else
        {
            _httpClient.BaseAddress = new Uri(_url!);
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _accessToken);
            logger.LogInformation("PortainerService initialized with URL: {Url}", _url);
        }
    }

    private async Task<PortainerStackDto?> GetStack(string stackName)
    {
        if (!IsConfigured)
            return null;

        var filters = JsonSerializer.Serialize(new { Name = stackName });
        var resp = await _httpClient.GetAsync($"/api/stacks?filters={filters}");

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Could not list Portainer stacks: {Status}. Check that {UrlKey} is reachable and access token is valid",
                resp.StatusCode,
                Constants.VariableKeys.PORTAINER_URL
            );
            return null;
        }

        var stacks = await resp.Content.ReadFromJsonAsync<PortainerStackDto[]>();

        if (stacks is null || stacks.Length == 0)
        {
            _logger.LogWarning(
                "No Portainer stacks found when searching with filters: {Filters}",
                filters
            );
            return null;
        }

        return stacks[0];
    }

    public async Task<string?> GetStackFileContentAsync(string stackName)
    {
        var first = await GetStack(stackName);

        if (first is null)
            return null;

        var fileResp = await _httpClient.GetAsync(
            $"/api/stacks/{first.Id}/file?endpointId={first.EndpointId}"
        );

        if (!fileResp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Could not get Portainer stack file: {Status}. Ensure {UrlKey} and access token are valid and stack exists.",
                fileResp.StatusCode,
                Constants.VariableKeys.PORTAINER_URL
            );
            return null;
        }

        var fileDto = await fileResp.Content.ReadFromJsonAsync<PortainerStackFileDto>();
        return fileDto?.StackFileContent;
    }

    public async Task UpdateStackFileContentAsync(string stackName, string newFileContent)
    {
        var first = await GetStack(stackName);

        if (first is null)
            throw new("No such stack found.");

        var payload = JsonSerializer.Serialize(
            new { stackFileContent = newFileContent, pullImage = true }
        );
        var putResp = await _httpClient.PutAsync(
            $"/api/stacks/{first.Id}?endpointId={first.EndpointId}",
            new StringContent(payload, Encoding.UTF8, "application/json")
        );

        if (!putResp.IsSuccessStatusCode)
            throw new(
                $"Could not update Portainer stack file: {putResp.StatusCode}, full response: {await putResp.Content.ReadAsStringAsync()}. Check {Constants.VariableKeys.PORTAINER_URL} and access token"
            );
    }
}
