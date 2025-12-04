using System.Text;
using System.Text.Json;

namespace PatchPanda.Web.Services;

public class AppriseService : IAppriseService
{
    private readonly string[] _urls;
    private readonly string? _appriseUrl;
    private readonly ILogger<AppriseService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly bool _isInitialized;

    public AppriseService(
        IConfiguration configuration,
        ILogger<AppriseService> logger,
        IHttpClientFactory httpClientFactory
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _logger = logger;
        _httpClientFactory = httpClientFactory;
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

        var targetUrl = $"{_appriseUrl}/notify";

        try
        {
            var httpClient = _httpClientFactory.CreateClient();

            var processedUrls = new List<string>(_urls.Length);
            for (int i = 0; i < _urls.Length; i++)
            {
                var url = _urls[i];
                List<string> additions = [];

                if (!url.Contains("/?"))
                    additions.Add("/?");

                if (!url.Contains("overflow="))
                    additions.Add("overflow=split");

                if (!url.Contains("emojis="))
                    additions.Add("emojis=yes");

                if (additions.Any())
                    url += string.Join('&', additions);

                processedUrls.Add(url);
            }
            var payload = new { body = message, urls = string.Join(',', processedUrls) };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var resp = await httpClient.PostAsync(targetUrl, content, cancellationToken);

            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new FailedNotificationException(targetUrl, ex);
        }
    }
}
