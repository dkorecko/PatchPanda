using System.Text;
using System.Text.Json;

namespace PatchPanda.Web.Services;

public class OllamaService : IAIService
{
    private static readonly JsonSerializerOptions CachedJsonSerializerOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly string? _endpoint;
    private readonly string? _model;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaService> _logger;
    private bool _isInitialized;

    public OllamaService(
        IConfiguration config,
        ILogger<OllamaService> logger,
        IHttpClientFactory httpClientFactory
    )
    {
        var endpoint = config[Constants.VariableKeys.OLLAMA_URL];
        var model = config[Constants.VariableKeys.OLLAMA_MODEL];

        _endpoint = endpoint;
        _model = model;
        _logger = logger;

        _httpClientFactory = httpClientFactory;

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning(
                "OllamaService not initialized because either {EndpointKey} or {ModelKey} were not configured.",
                Constants.VariableKeys.OLLAMA_URL,
                Constants.VariableKeys.OLLAMA_MODEL
            );
            return;
        }

        _isInitialized = true;
        _logger.LogInformation(
            "OllamaService configured with endpoint {Endpoint} and model {Model}.",
            endpoint,
            model
        );
    }

    public bool IsInitialized() => _isInitialized;

    public async Task<AIResult?> SummarizeReleaseNotes(string releaseNotes)
    {
        if (!_isInitialized)
            return null;

        using var httpClient = _httpClientFactory.CreateClient();

        var prompt =
            $"Summarize the following release notes in one short paragraph (3-5 sentences), formulated for the people self-hosting this application. Only highlight important changes and cool new features. Also, determine if there are any breaking changes. \n\nRelease notes:\n{releaseNotes}\n\n **RESPOND IN THE PROVIDED JSON FORMAT** You MUST respond in JSON no matter the circumstance: {{\"summary\": string, \"breaking\": bool}}.";

        var request = new
        {
            model = _model,
            prompt,
            stream = false
        };
        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json"
        );

        try
        {
            var response = await httpClient.PostAsync(_endpoint + "/api/generate", content);
            response.EnsureSuccessStatusCode();

            var ollamaResult = await response.Content.ReadFromJsonAsync<OllamaResult>(
                CachedJsonSerializerOptions
            );
            _logger.LogDebug("Ollama response: {Response}", ollamaResult?.Response);
            _logger.LogDebug("Ollama thinking: {Thinking}", ollamaResult?.Thinking);
            var innerResponse = JsonSerializer.Deserialize<AIResult>(
                ollamaResult?.Response ?? string.Empty,
                CachedJsonSerializerOptions
            );
            return innerResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "There was an error while contacting the Ollama API.");
            return null;
        }
    }

    public class OllamaResult
    {
        public required string Response { get; set; }

        public string? Thinking { get; set; }
    }
}
