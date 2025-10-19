using System.Text;
using System.Text.Json;

namespace PatchPanda.Web.Services;

public class DiscordService
{
    private string WebhookUrl { get; init; }

    public DiscordService(IConfiguration configuration)
    {
        var webhookUrl = configuration.GetValue<string>("DISCORD_WEBHOOK_URL")!;

        if (webhookUrl is null)
            throw new InvalidOperationException("DISCORD_WEBHOOK_URL configuration is missing.");

        WebhookUrl = webhookUrl;
    }

    public async Task SendUpdates(ComposeApp app, string[] otherNames)
    {
        var message = new StringBuilder();

        message.AppendLine(
            $":tada: **{string.Join(" + ", [app, .. otherNames])} UPDATE** :tada:\n"
        );
        message.AppendLine(":rocket: **Version Details**");
        message.AppendLine($"- **New Version:** `{app.NewerVersions.First().VersionNumber}`");
        message.AppendLine($"- **Previously Used Version:** `{app.Version}`");
        message.AppendLine(
            $"- **Breaking Change:** {(app.NewerVersions.Any(x => x.Breaking) ? "Yes :x:" : "No :white_check_mark:")}"
        );
        message.AppendLine(
            $"- **Prerelease:** {(app.NewerVersions.Any(x => x.Prerelease) ? "Yes :x:" : "No :white_check_mark:")}"
        );

        message.AppendLine("\n");

        foreach (var newVersion in app.NewerVersions)
        {
            message.AppendLine(
                $":scroll: **Release Notes - {newVersion.VersionNumber} {(newVersion.Prerelease ? "[PRERELEASE]" : string.Empty)} {(newVersion.Breaking ? "[BREAKING]" : string.Empty)}**\n"
            );
            message.AppendLine(newVersion.Body);
            message.AppendLine("\n");
            newVersion.Notified = true;
        }

        message.AppendLine($"\n{app.GitHubRepo}");

        var fullMessage = message.ToString();
        const int ChunkSize = 2000;

        while (fullMessage.Length > 0)
        {
            var msg = fullMessage.Length > ChunkSize ? fullMessage[..ChunkSize] : fullMessage;
            await SendWebhook(msg);
            fullMessage = fullMessage.Length > ChunkSize ? fullMessage[ChunkSize..] : string.Empty;
            await Task.Delay(1000);
        }
    }

    public async Task SendWebhook(string content)
    {
        using var httpClient = new HttpClient();
        var payload = new
        {
            content,
            username = "PatchPanda",
            flags = 4
        };
        var jsonPayload = JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(
            jsonPayload,
            System.Text.Encoding.UTF8,
            "application/json"
        );
        var response = await httpClient.PostAsync(WebhookUrl, httpContent);
        response.EnsureSuccessStatusCode();
    }
}
