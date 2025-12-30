using System.Text;
using System.Text.Json;

namespace PatchPanda.Web.Services;

public class OllamaService : IAiService
{
    private static readonly JsonSerializerOptions CachedJsonSerializerOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly string? _endpoint;
    private readonly string? _model;
    private readonly int _contextSize = 32768;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaService> _logger;
    private readonly bool _isInitialized;

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

        var contextSize = config[Constants.VariableKeys.OLLAMA_NUM_CTX];
        
        if (contextSize is not null && int.TryParse(contextSize, out var contextSizeInt))
            _contextSize = contextSizeInt;

        _isInitialized = true;
        _logger.LogInformation(
            "OllamaService configured with endpoint {Endpoint}, model {Model} and context size {ContextSize}.",
            endpoint,
            model,
            _contextSize
        );
    }

    public bool IsInitialized() => _isInitialized;

    private async Task<T?> SendPrompt<T>(string prompt) where T : class, IAiResult
    {
        if (!_isInitialized)
            return null;

        using var httpClient = _httpClientFactory.CreateClient();
        
        var request = new
        {
            model = _model,
            prompt,
            stream = false,
            options = new
            {
                num_ctx = _contextSize
            }
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
            var innerResponse = JsonSerializer.Deserialize<T>(
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

    public async Task<SummaryResult?> SummarizeReleaseNotes(string releaseNotes)
        => await SendPrompt<SummaryResult>(
            $"Summarize the following release notes in one short paragraph (3-5 sentences), formulated for the people self-hosting this application. Only highlight important changes and cool new features. Also, determine if there are any breaking changes. \n\nRelease notes:\n{releaseNotes}\n\n **RESPOND IN THE PROVIDED JSON FORMAT** You MUST respond in JSON no matter the circumstance: {{\"summary\": string, \"breaking\": bool}}.");

    public async Task<SecurityAnalysisResult?> AnalyzeDiff(string diff)
    {
        if (!_isInitialized)
            return null;

        // Estimate character limit per chunk (tokens to chars, conservative 1 token = 2.5 chars)
        // Leave room for the prompt - 200
        var maxCharsPerChunk = (int)((_contextSize - 200) * 2.5);
        if (maxCharsPerChunk <= 0) maxCharsPerChunk = 4096; // Fallback

        if (diff.Length <= maxCharsPerChunk)
        {
            return await SendPrompt<SecurityAnalysisResult>(
                $"You are a security expert. Analyze the following git diff for any malicious code, backdoors, obfuscated code, or suspicious network calls. \n\nDiff:\n{diff}\n\n **RESPOND IN THE PROVIDED JSON FORMAT** You MUST respond in JSON no matter the circumstance: {{\"analysis\": string (short summary of findings), \"isSuspectedMalicious\": bool}}.");
        }

        _logger.LogInformation("Diff is too large ({Length} chars), splitting into chunks...", diff.Length);

        var chunks = new List<string>();
        for (var i = 0; i < diff.Length; i += maxCharsPerChunk)
        {
            chunks.Add(diff.Substring(i, Math.Min(maxCharsPerChunk, diff.Length - i)));
        }

        var analyses = new List<string>();
        var isAnyMalicious = false;

        for (int i = 0; i < chunks.Count; i++)
        {
            _logger.LogInformation("Analyzing chunk {Current}/{Total}...", i + 1, chunks.Count);
            var result = await SendPrompt<SecurityAnalysisResult>(
                $"You are a security expert. Analyze the following chunk ({i + 1}/{chunks.Count}) of a git diff for any malicious code, backdoors, obfuscated code, or suspicious network calls. \n\nDiff Chunk:\n{chunks[i]}\n\n **RESPOND IN THE PROVIDED JSON FORMAT** You MUST respond in JSON no matter the circumstance: {{\"analysis\": string (short summary of findings), \"isSuspectedMalicious\": bool}}.");

            if (result == null) 
                continue;
            
            analyses.Add($"Chunk {i + 1}: {result.Analysis}");
            if (result.IsSuspectedMalicious)
                isAnyMalicious = true;
        }

        return new SecurityAnalysisResult
        {
            Analysis = string.Join("\n\n", analyses),
            IsSuspectedMalicious = isAnyMalicious
        };
    }

    public class OllamaResult
    {
        public required string Response { get; set; }

        public string? Thinking { get; set; }
    }
    
}
