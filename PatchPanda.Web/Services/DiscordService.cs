using System.Text;
using System.Text.Json;

namespace PatchPanda.Web.Services;

public class DiscordService
{
    private string WebhookUrl { get; init; }

    private readonly IDbContextFactory<DataContext> _dbContextFactory;

    public DiscordService(
        IConfiguration configuration,
        IDbContextFactory<DataContext> dbContextFactory
    )
    {
        var webhookUrl = configuration.GetValue<string>("DISCORD_WEBHOOK_URL")!;

        if (webhookUrl is null)
            throw new InvalidOperationException("DISCORD_WEBHOOK_URL configuration is missing.");

        WebhookUrl = webhookUrl;
        _dbContextFactory = dbContextFactory;
    }

    public async Task SendUpdates(Container container, Container[] otherContainers)
    {
        using var db = _dbContextFactory.CreateDbContext();

        var newerVersions = (
            await db.Containers.Include(x => x.NewerVersions).FirstAsync(x => x.Id == container.Id)
        ).NewerVersions.Where(x => !x.Notified);

        var message = new StringBuilder();

        message.AppendLine(
            $"# 🎉 {string.Join(" + ", [container.Name, .. otherContainers.Select(x => x.Name)])} UPDATE 🎉\n"
        );
        message.AppendLine("🚀 **Version Details**");
        message.AppendLine($"- **New Version:** `{newerVersions.First().VersionNumber}`");
        message.AppendLine($"- **Previously Used Version:** `{container.Version ?? "Missing"}`");
        message.AppendLine(
            $"- **Breaking Change:** {(newerVersions.Any(x => x.Breaking) ? "Yes :x:" : "No :white_check_mark:")}"
        );
        message.AppendLine(
            $"- **Prerelease:** {(newerVersions.Any(x => x.Prerelease) ? "Yes :x:" : "No :white_check_mark:")}"
        );

        message.AppendLine("\n");

        foreach (var newVersion in newerVersions)
        {
            message.AppendLine(
                $"## 📜 Release Notes - {newVersion.VersionNumber} {(newVersion.Prerelease ? "[PRERELEASE]" : string.Empty)} {(newVersion.Breaking ? "[BREAKING]" : string.Empty)}\n"
            );
            message.AppendLine(newVersion.Body);
            message.AppendLine("\n");
            newVersion.Notified = true;
        }

        var repo = container.GetGitHubRepo();
        message.AppendLine($"\nhttps://github.com/{repo!.Item1}/{repo.Item2}/releases");
        message.AppendLine(
            $"\n__Verify and Update Here:__ http://trixx.falcon-bass.ts.net:5091/versions/{container.Id}"
        );

        var fullMessage = message.ToString();
        const int ChunkSize = 2000;

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

        await db.SaveChangesAsync();
    }

    public async Task SendWebhook(string content)
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
