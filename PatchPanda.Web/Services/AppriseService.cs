using System.Text;
using System.Text.Json;

namespace PatchPanda.Web.Services;

public class AppriseService
{
    private readonly string[] _urls;
    private readonly string? _appriseUrl;
    private readonly ILogger<AppriseService> _logger;
    private readonly bool _isInitialized;

    public AppriseService(IConfiguration configuration, ILogger<AppriseService> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _urls = [];

        var appriseApiUrl = configuration.GetValue<string?>(Constants.VariableKeys.APPRISE_API_URL);

        if (string.IsNullOrWhiteSpace(appriseApiUrl))
        {
            _logger.LogInformation(
                "{NotificationUrlsKey} variable is missing, AppriseService is not initialized.",
                Constants.VariableKeys.APPRISE_API_URL
            );
            return;
        }

        var notificationUrlsRaw = configuration.GetValue<string?>(
            Constants.VariableKeys.APPRISE_NOTIFICATION_URLS
        );

        if (string.IsNullOrWhiteSpace(notificationUrlsRaw))
        {
            _logger.LogInformation(
                "{NotificationUrlsKey} variable is missing, AppriseService is not initialized.",
                Constants.VariableKeys.APPRISE_NOTIFICATION_URLS
            );
            return;
        }

        _appriseUrl = appriseApiUrl.TrimEnd('/');

        var parts = notificationUrlsRaw
            .Split([';', '\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        _urls = parts;
        _isInitialized = _urls.Length > 0;

        _logger.LogInformation("AppriseService initialized with {Count} endpoints.", _urls.Length);
    }

    public bool IsInitialized => _isInitialized;

    public IReadOnlyList<string> GetEndpoints() => _urls;

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized || _appriseUrl is null)
            return;

        using var httpClient = new HttpClient();

        var payload = new { body = message, urls = string.Join(',', _urls) };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await httpClient.PostAsync($"{_appriseUrl}/notify", content, cancellationToken);

        resp.EnsureSuccessStatusCode();
    }
}
