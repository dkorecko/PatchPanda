using System.Text;
using System.Text.Json;

namespace PatchPanda.Web.Services;

public class OllamaService : IAiService
{
    private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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

    private async Task<T?> SendPrompt<T>(string prompt, string? enforceFormat = null)
        where T : class, IAiResult
    {
        if (!_isInitialized)
            return null;

        using var httpClient = _httpClientFactory.CreateClient();

        var enforceFormatString = enforceFormat is not null
            ? $"\n\n**RESPOND IN THE PROVIDED JSON FORMAT** You MUST respond in JSON no matter the circumstance: {enforceFormat}"
            : null;

        var request = new
        {
            model = _model,
            prompt = prompt + (enforceFormatString ?? string.Empty),
            stream = false,
            options = new { num_ctx = _contextSize },
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

    private async Task<T?> SendPromptWithChunking<T>(
        string text,
        Func<string, string> buildPrompt,
        Func<T, string> extractChunkSummary,
        Func<List<string>, string> buildFinalPrompt,
        Func<List<T>, T> buildFallback,
        string? jsonTemplate = null
    )
        where T : class, IAiResult
    {
        if (!_isInitialized)
            return null;

        // Estimate character limit per chunk (tokens to chars, conservative 1 token = 2.5 chars)
        // Leave room for the prompt - 200
        var maxCharsPerChunk = (int)((_contextSize - 200) * 2.5);

        if (maxCharsPerChunk <= 0)
            maxCharsPerChunk = 4096; // Fallback

        if (text.Length <= maxCharsPerChunk)
            return await SendPrompt<T>(buildPrompt(text), jsonTemplate);

        _logger.LogInformation(
            "Text is too large ({Length} chars), splitting into chunks...",
            text.Length
        );

        var chunks = new List<string>();
        for (var i = 0; i < text.Length; i += maxCharsPerChunk)
        {
            chunks.Add(text.Substring(i, Math.Min(maxCharsPerChunk, text.Length - i)));
        }
        var perChunkSummaries = new List<string>();
        var perChunkResults = new List<T>();

        for (var i = 0; i < chunks.Count; i++)
        {
            _logger.LogInformation("Processing chunk {Current}/{Total}...", i + 1, chunks.Count);
            var result = await SendPrompt<T>(buildPrompt(chunks[i]), jsonTemplate);
            if (result == null)
                continue;

            perChunkResults.Add(result);
            perChunkSummaries.Add($"Chunk {i + 1}: {extractChunkSummary(result)}");
        }

        if (chunks.Count <= 1)
            return buildFallback(perChunkResults);

        _logger.LogInformation("Requesting final summary of all chunk summaries from the model...");

        var finalPrompt = buildFinalPrompt(perChunkSummaries);
        var finalResult = await SendPrompt<T>(finalPrompt, jsonTemplate);

        return finalResult ?? buildFallback(perChunkResults);
    }

    public async Task<SummaryResult?> SummarizeReleaseNotes(string releaseNotes)
    {
        return await SendPromptWithChunking<SummaryResult>(
            releaseNotes,
            whole =>
                $"Summarize the following release notes in one short paragraph (3-5 sentences), formulated for the people self-hosting this application. Only highlight important changes and cool new features. Also, determine if there are any breaking changes. \n\nRelease notes:\n{whole}",
            r => r.Summary,
            perChunkSummaries =>
                $"""
                Summarize the following release notes that were analyzed in multiple chunks. Provide a single consolidated summary (1 short paragraph, 3-5 sentences) suitable for self-hosting users, and indicate whether the release contains any breaking changes. Be concise.

                Per-chunk summaries:
                {string.Join("\n\n", perChunkSummaries)}
                """,
            results =>
                new()
                {
                    Summary = string.Join("\n\n", results.Select(r => r.Summary)),
                    Breaking = results.Any(r => r.Breaking),
                },
            "{\"summary\": string, \"breaking\": bool}"
        );
    }

    public async Task<SecurityAnalysisResult?> AnalyzeDiff(string diff)
    {
        return await SendPromptWithChunking<SecurityAnalysisResult>(
            diff,
            whole =>
                $"You are a security expert. Analyze the following git diff for any malicious code, backdoors, obfuscated code, or suspicious network calls. \n\nDiff:\n{whole}",
            r => r.Analysis,
            perChunkSummaries =>
                $"""
                You are a security expert. The git diff was too large and was analyzed in multiple chunks. Below are the per-chunk short findings. Please provide a single consolidated analysis combining these findings, and indicate whether the overall diff is suspected malicious. Be concise.

                Per-chunk findings:
                {string.Join("\n\n", perChunkSummaries)}
                """,
            results => new SecurityAnalysisResult
            {
                Analysis = string.Join("\n\n", results.Select(r => r.Analysis)),
                IsSuspectedMalicious = results.Any(r => r.IsSuspectedMalicious),
            },
            "{\"analysis\": string (short summary of findings), \"isSuspectedMalicious\": bool}"
        );
    }

    public class OllamaResult
    {
        public required string Response { get; set; }
        public string? Thinking { get; set; }
    }
}
