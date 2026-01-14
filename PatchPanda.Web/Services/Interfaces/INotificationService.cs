namespace PatchPanda.Web.Services.Interfaces;

public interface INotificationService
{
    bool AnyInitialized { get; }

    List<string> GetEndpoints();

    Task SendAutoUpdateResult(
        Container container,
        string targetVersion,
        bool success,
        bool isAutomatic,
        string? errorMessage = null
    );

    Task<bool> SendNewVersion(
        Container mainApp,
        List<Container> otherApps,
        List<AppVersion> newerVersions
    );

    /// <summary>
    /// Sends a notification message via all initialized services.
    /// </summary>
    /// <param name="message">Message to send</param>
    /// <param name="propagateExceptions">Whether any failures should throw. If false, it will log failures instead of throwing</param>
    /// <param name="noSuccessIsError">Whether it should throw error when sending notification was not successful</param>
    /// <returns>Boolean determining if any notifications were successful</returns>
    Task<bool> TrySendNotification(
        string message,
        bool propagateExceptions = false,
        bool noSuccessIsError = true
    );

    /// <summary>
    /// Sends a notification message via all initialized services, but throws if none succeed and if any of them fail.
    /// </summary>
    /// <param name="message">Message to send</param>
    Task SendNotification(string message);
}
