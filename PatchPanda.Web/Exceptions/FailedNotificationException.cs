namespace PatchPanda.Web.Exceptions;

public class FailedNotificationException(string notificationUrl, Exception innerException)
    : Exception($"Failed to send notification to {notificationUrl}", innerException) { }
