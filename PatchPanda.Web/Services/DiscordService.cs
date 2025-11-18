using System.Text;
using System.Text.Json;

namespace PatchPanda.Web.Services;

public class DiscordService
{
    public string? WebhookUrl { get; }

    private readonly IDbContextFactory<DataContext> _dbContextFactory;
    private readonly bool _isInitialized;

    public DiscordService(
        IConfiguration configuration,
        IDbContextFactory<DataContext> dbContextFactory,
        ILogger<DiscordService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _dbContextFactory = dbContextFactory;

        var webhookUrl = configuration.GetValue<string>(
            Constants.VariableKeys.DISCORD_WEBHOOK_URL
        )!;
        logger.LogInformation(
            "{WebhookKey}={WebhookUrl}",
            Constants.VariableKeys.DISCORD_WEBHOOK_URL,
            webhookUrl
        );

        WebhookUrl = webhookUrl;

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _isInitialized = false;
            logger.LogInformation(
                "{WebhookKey} configuration is missing, DiscordService is not initialized.",
                Constants.VariableKeys.DISCORD_WEBHOOK_URL
            );
        }
        else
        {
            _isInitialized = true;
            logger.LogInformation("DiscordService is initialized");
        }
    }

    public bool IsInitialized => _isInitialized;

    public async Task SendRawAsync(string content)
    {
        if (!_isInitialized || WebhookUrl is null)
            return;

        try
        {
            const int ChunkSize = 2000;

            var fullMessage = content;

            while (fullMessage.Length > 0)
            {
                var splitPoint = Math.Min(ChunkSize, fullMessage.Length);
                var msg = string.Empty;

                if (splitPoint < fullMessage.Length)
                {
                    var lastNewlineIndex = fullMessage.LastIndexOfAny(
                        ['\n', '\r'],
                        splitPoint,
                        splitPoint - 1
                    );
                    if (lastNewlineIndex != -1)
                        splitPoint = lastNewlineIndex + 1;
                }

                msg = fullMessage[..splitPoint];
                await SendWebhook(msg);
                fullMessage =
                    fullMessage.Length > splitPoint ? fullMessage[splitPoint..] : string.Empty;
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            throw new FailedNotificationException(WebhookUrl, ex);
        }
    }

    private async Task SendWebhook(string content)
    {
        using var httpClient = new HttpClient();
        var payload = new
        {
            content,
            username = Constants.APP_NAME,
            flags = 4
        };
        var jsonPayload = JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(WebhookUrl, httpContent);
        response.EnsureSuccessStatusCode();
    }
}
