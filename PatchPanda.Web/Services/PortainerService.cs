using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PatchPanda.Web.DTOs;

namespace PatchPanda.Web.Services;

public class PortainerService : IPortainerService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PortainerService> _logger;
    private readonly string? _url;
    private readonly string? _username;
    private readonly string? _password;
    private string? _jwt;
    private DateTime? _jwtExpiry;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_url)
        && !string.IsNullOrWhiteSpace(_username)
        && !string.IsNullOrWhiteSpace(_password);

    public PortainerService(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<PortainerService> logger
    )
    {
        _logger = logger;
        _url = configuration.GetValue<string?>(Constants.VariableKeys.PORTAINER_URL);
        _username = configuration.GetValue<string?>(Constants.VariableKeys.PORTAINER_USERNAME);
        _password = configuration.GetValue<string?>(Constants.VariableKeys.PORTAINER_PASSWORD);
        _httpClient = httpFactory.CreateClient();

        if (!IsConfigured)
        {
            logger.LogWarning(
                "{Url}, {Username} or {Password} is missing. Please set all three if you wish to enable Portainer integration.",
                Constants.VariableKeys.PORTAINER_URL,
                Constants.VariableKeys.PORTAINER_USERNAME,
                Constants.VariableKeys.PORTAINER_PASSWORD
            );
        }
        else
        {
            _httpClient.BaseAddress = new Uri(_url!);
            logger.LogInformation("PortainerService initialized with URL: {Url}", _url);
        }
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (!IsConfigured)
            return;

        if (
            !string.IsNullOrWhiteSpace(_jwt)
            && _jwtExpiry is not null
            && _jwtExpiry > DateTime.UtcNow
        )
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _jwt
            );
            return;
        }

        _logger.LogInformation("Using username and password to authenticate with Portainer...");

        var payload = JsonSerializer.Serialize(new { username = _username, password = _password });
        var resp = await _httpClient.PostAsync(
            "/api/auth",
            new StringContent(payload, Encoding.UTF8, "application/json")
        );

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Portainer authentication failed with status {Status}. Make sure {UrlKey}, {UserKey} and {PassKey} are set correctly.",
                resp.StatusCode,
                Constants.VariableKeys.PORTAINER_URL,
                Constants.VariableKeys.PORTAINER_USERNAME,
                Constants.VariableKeys.PORTAINER_PASSWORD
            );
            return;
        }

        var json = await resp.Content.ReadFromJsonAsync<PortainerAuthResponse>();

        if (json?.Jwt is not null)
        {
            _jwt = json.Jwt;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _jwt
            );
            _jwtExpiry = JwtHelper.GetJwtExpiry(_jwt) ?? DateTime.UtcNow.AddHours(8);
            _logger.LogInformation("Portainer authentication successful.");
        }
        else
            _logger.LogWarning("Failed parsing Portainer auth response.");
    }

    private async Task<PortainerStackDto?> GetStack(string stackName)
    {
        if (!IsConfigured)
            return null;

        await EnsureAuthenticatedAsync();

        var filters = JsonSerializer.Serialize(new { Name = stackName });
        var resp = await _httpClient.GetAsync($"/api/stacks?filters={filters}");

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Could not list Portainer stacks: {Status}. Check that {UrlKey} is reachable and credentials are valid",
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
                "Could not get Portainer stack file: {Status}. Ensure {UrlKey} and credentials are valid and stack exists.",
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
                $"Could not update Portainer stack file: {putResp.StatusCode}, full response: {await putResp.Content.ReadAsStringAsync()}. Check {Constants.VariableKeys.PORTAINER_URL} and credentials"
            );
    }
}
