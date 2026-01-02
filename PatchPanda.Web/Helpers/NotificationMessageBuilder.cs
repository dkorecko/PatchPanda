using System.Text;

namespace PatchPanda.Web.Helpers;

public static class NotificationMessageBuilder
{
    public static string Build(
        Container mainApp,
        List<Container> otherApps,
        List<AppVersion> newerVersions
    )
    {
        var message = new StringBuilder();

        message.AppendLine(
            $"# 🎉 {string.Join(" + ", [mainApp.Name, .. otherApps.Select(x => x.Name)])} UPDATE 🎉\n"
        );
        message.AppendLine("🚀 **Version Details**");
        var newestVersion = newerVersions.First();
        message.AppendLine($"- **New Version:** `{newestVersion.VersionNumber}`");
        message.AppendLine($"- **Previously Used Version:** `{mainApp.Version ?? "Missing"}`");
        message.AppendLine(
            $"- **Breaking Change Algorithm:** {(newerVersions.Any(x => x.Breaking) ? "Yes :x:" : "No :white_check_mark:")}"
        );
        if (newerVersions.Any(x => x.AIBreaking is not null))
        {
            message.AppendLine(
                $"- **Breaking Change AI:** {(newerVersions.Any(x => x.AIBreaking == true) ? "Yes :x:" : "No :white_check_mark:")}"
            );
        }

        if (newerVersions.Any(x => x.IsSuspectedMalicious is not null))
        {
            message.AppendLine(
                $"- **Suspected Malicious by AI:** {(newerVersions.Any(x => x.IsSuspectedMalicious == true) ? "Yes :x:" : "No :white_check_mark:")}"
            );
        }
        message.AppendLine(
            $"- **Prerelease:** {(newerVersions.Any(x => x.Prerelease) ? "Yes :x:" : "No :white_check_mark:")}"
        );

        message.AppendLine("\n");

        foreach (var newVersion in newerVersions)
        {
            var securityBadge = newVersion.IsSuspectedMalicious == true ? " [POSSIBLE MALICIOUS]" : string.Empty;
            message.AppendLine(
                $"## 📜 Release Notes - {newVersion.VersionNumber} {(newVersion.Prerelease ? "[PRERELEASE]" : string.Empty)} {(newVersion.Breaking ? "[BREAKING]" : string.Empty)} {(newVersion.AIBreaking == true ? "[AI BREAKING]" : string.Empty)}{securityBadge}"
            );
            if (!string.IsNullOrWhiteSpace(newVersion.SecurityAnalysis))
            {
                message.AppendLine($"**Security Analysis:** {newVersion.SecurityAnalysis}");
            }
            if (!string.IsNullOrWhiteSpace(newVersion.AISummary))
            {
                message.AppendLine($"**AI Summary:** {newVersion.AISummary}");
            }
            message.AppendLine(newVersion.Body);
            message.AppendLine("\n");
        }

        var repo = mainApp.GetGitHubRepo();

        if (repo is not null)
            message.AppendLine($"\nhttps://github.com/{repo.Item1}/{repo.Item2}/releases");

        if (Constants.BASE_URL is not null)
            message.AppendLine(
                $"\n__Verify and Update Here:__ {Constants.BASE_URL}/versions/{mainApp.Id}"
            );
        else
            message.AppendLine(
                "\n__**BASE URL was missing, therefore an update URL cannot be provided.**__"
            );

        return message.ToString();
    }

    public static string BuildAutoUpdateResult(
        Container container,
        string targetVersion,
        bool success,
        string? errorMessage = null
    )
    {
        var message = new StringBuilder();

        if (success)
        {
            message.AppendLine($"# Automatic Update Successful: {container.Name} ✅\n");
            message.AppendLine($"The application has been successfully updated to version `{targetVersion}`. More details can be viewed in the update attempts panel.");
        }
        else
        {
            message.AppendLine($"# ❌ Automatic Update Failed: {container.Name} ❌\n");
            message.AppendLine($"An attempt to automatically update to version `{targetVersion}` failed. More details can be viewed in the update attempts panel.");

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                message.AppendLine($"\n**Error Details:**\n{errorMessage}");
            }
        }

        if (Constants.BASE_URL is not null)
            message.AppendLine($"\n__View Container Details:__ {Constants.BASE_URL}/versions/{container.Id}");

        return message.ToString();
    }
}
