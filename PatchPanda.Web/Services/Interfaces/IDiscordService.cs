namespace PatchPanda.Web.Services.Interfaces;

public interface IDiscordService
{
    public string? WebhookUrl { get; }
    public bool IsInitialized { get; }
    public Task SendRawAsync(string content);
}
