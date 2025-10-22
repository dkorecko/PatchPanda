using System.Text.Json;
using System.Text.RegularExpressions;

namespace PatchPanda.Web.Helpers;

public static class ParsingHelper
{
    public static async Task SetGitHubRepo(
        this Container container,
        ContainerListResponse response,
        VersionService versionService,
        ILogger logger
    )
    {
        List<string> repos = [];

        var fullResponse = JsonSerializer.Serialize(response);

        var matches = Regex
            .Matches(fullResponse, @"https://github.com\/[a-zA-Z0-9_-]+\/[a-zA-Z0-9_-]+")
            .Select(x => x.Value)
            .Select(CleanUpUrl)
            .Distinct();

        foreach (var match in matches)
        {
            container.GitHubRepo = match;
            try
            {
                var versions = await versionService.GetNewerVersions(container, []);

                if (versions.Any())
                    return;
            }
            catch
            {
                logger.LogInformation("Failed to get versions for combination {Match}", match);
            }
        }
    }

    private static string CleanUpUrl(string input) =>
        "https://" + string.Join('/', input.Replace("https://", string.Empty).Split('/').Take(3));
}
