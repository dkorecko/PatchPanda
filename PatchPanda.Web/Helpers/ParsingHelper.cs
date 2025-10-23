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

        var githubMatches = Regex.Matches(
            fullResponse,
            @"https://github.com\/([a-zA-Z0-9_-]+)\/([a-zA-Z0-9_-]+)"
        );

        var ghcrMatches = Regex.Matches(
            fullResponse,
            @"ghcr.io\/([a-zA-Z0-9_-]+)\/([a-zA-Z0-9_-]+)"
        );

        var imageMatches = Regex.Matches(response.Image, @"([a-zA-Z0-9_-]+)\/([a-zA-Z0-9_-]+):");

        Dictionary<Tuple<string, string>, IReadOnlyList<Octokit.Release>> versionCounts = [];

        foreach (
            var match in githubMatches
                .Concat(ghcrMatches)
                .Concat(imageMatches)
                .Select(x => new Tuple<string, string>(
                    x.Groups[1].Value.ToLower(),
                    x.Groups[2].Value.ToLower()
                ))
                .Distinct()
        )
        {
            try
            {
                var versions = await versionService.GetVersions(match);

                versionCounts.Add(match, versions);
            }
            catch
            {
                logger.LogInformation("Failed to get versions for combination {Match}", match);
            }
        }

        if (versionCounts.Any())
        {
            var bestChoice = versionCounts.MaxBy(x => x.Value.Count);
            container.GitHubRepo = bestChoice.Key;
            container.GitHubVersionRegex = VersionHelper.BuildRegexFromVersion(
                bestChoice.Value.First().TagName
            );

            if (versionCounts.Count > 1)
            {
                container.SecondaryGitHubRepos = versionCounts
                    .Where(x => x.Key != bestChoice.Key)
                    .Select(x => x.Key)
                    .ToList();
            }
        }
    }
}
