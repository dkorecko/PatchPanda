namespace PatchPanda.Web.Services;

public class NotificationService(
    IDiscordService discordService,
    IAppriseService appriseService,
    ILogger<NotificationService> logger
) : INotificationService
{
    public bool AnyInitialized => discordService.IsInitialized || appriseService.IsInitialized;

    public List<string> GetEndpoints()
    {
        List<string> endpoints = [];

        if (discordService.IsInitialized && !string.IsNullOrWhiteSpace(discordService.WebhookUrl))
            endpoints.Add(discordService.WebhookUrl);

        var appriseEndpoints = appriseService.IsInitialized ? appriseService.GetEndpoints() : [];

        if (appriseEndpoints.Any())
            endpoints.AddRange(appriseEndpoints);

        return endpoints;
    }

    public async Task SendAutoUpdateResult(
        Container container,
        string targetVersion,
        bool success,
        bool isAutomatic,
        string? errorMessage = null
    )
    {
        if (!isAutomatic)
            return;

        var message = NotificationMessageBuilder.BuildAutoUpdateResult(
            container,
            targetVersion,
            success,
            errorMessage
        );

        var result = await TrySendNotification(message);

        if (!result)
            logger.LogError(
                "Failed to send auto-update notification for container {ContainerName}",
                container.Name
            );
    }

    public async Task<bool> SendNewVersion(
        Container mainApp,
        List<Container> otherApps,
        List<AppVersion> newerVersions
    )
    {
        var message = NotificationMessageBuilder.BuildNewVersion(mainApp, otherApps, newerVersions);

        return await TrySendNotification(message);
    }

    public async Task<bool> TrySendNotification(string message, bool propagateExceptions = false)
    {
        var success = 0;
        if (discordService.IsInitialized)
        {
            try
            {
                await discordService.SendRawAsync(message);
                success++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Discord notification failed");
                if (propagateExceptions)
                    throw;
            }
        }

        if (appriseService.IsInitialized)
        {
            try
            {
                await appriseService.SendAsync(message);
                success++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Apprise notification failed");
                if (propagateExceptions)
                    throw;
            }
        }

        if (success != 0)
            return true;

        if (!AnyInitialized)
            logger.LogWarning("No notification services are initialized. Message was not sent.");
        else
            logger.LogError("The notification could not be sent successfully.");

        return false;
    }

    public async Task SendNotification(string message)
    {
        var result = await TrySendNotification(message, true);

        if (!result)
            throw new Exception(
                "No notification services have been initialized. Check logs for details."
            );
    }
}
