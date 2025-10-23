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

        var githubMatches = Regex
            .Matches(fullResponse, @"https://github.com\/[a-zA-Z0-9_-]+\/[a-zA-Z0-9_-]+")
            .Select(x => x.Value)
            .Select(CleanUpUrl)
            .Distinct();

        var ghcrMatches = Regex
            .Matches(fullResponse, @"ghcr.io\/[a-zA-Z0-9_-]+\/[a-zA-Z0-9_-]+")
            .Select(x => CleanUpUrl(x.Value))
            .Distinct();

        Dictionary<string, int> versionCounts = [];

        foreach (var match in githubMatches.Concat(ghcrMatches).Distinct())
        {
            try
            {
                var versions = await versionService.GetVersions(match);

                versionCounts.Add(match, versions.Count);
            }
            catch
            {
                logger.LogInformation("Failed to get versions for combination {Match}", match);
            }
        }

        if (versionCounts.Any())
            container.GitHubRepo = versionCounts.MaxBy(x => x.Value).Key;
    }

    private static string CleanUpUrl(string input) =>
        "https://" + string.Join('/', input.Replace("https://", string.Empty).Split('/').Take(3));
}
