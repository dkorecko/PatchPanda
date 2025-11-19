using System.Text.Json;
using System.Text.RegularExpressions;

namespace PatchPanda.Web.Helpers;

public static class ParsingHelper
{
    private static Dictionary<Tuple<string, string>, IReadOnlyList<Octokit.Release>> DeduplicateRepositories(
        Dictionary<Tuple<string, string>, IReadOnlyList<Octokit.Release>> versionCounts
    )
    {
        var result = new Dictionary<Tuple<string, string>, IReadOnlyList<Octokit.Release>>();
        var processed = new HashSet<Tuple<string, string>>();

        foreach (var entry in versionCounts)
        {
            if (processed.Contains(entry.Key))
                continue;

            var duplicates = versionCounts
                .Where(other =>
                    !processed.Contains(other.Key)
                    && AreSameReleases(entry.Value, other.Value)
                )
                .ToList();

            var canonical = duplicates
                .Select(d => d.Key)
                .OrderByDescending(repo => repo.Item2.Length)
                .ThenByDescending(repo => repo.Item2.Contains("docker-"))
                .First();

            result[canonical] = entry.Value;

            foreach (var dup in duplicates)
            {
                processed.Add(dup.Key);
            }
        }

        return result;
    }

    private static bool AreSameReleases(
        IReadOnlyList<Octokit.Release> releases1,
        IReadOnlyList<Octokit.Release> releases2
    )
    {
        if (releases1.Count != releases2.Count)
            return false;

        if (releases1.Count == 0)
            return false;

        for (int i = 0; i < Math.Min(5, releases1.Count); i++)
        {
            if (releases1[i].Id != releases2[i].Id)
                return false;
        }

        return true;
    }

    public static async Task SetGitHubRepo(
        this Container container,
        ContainerListResponse response,
        VersionService versionService,
        ILogger logger
    )
    {
        if (container.OverrideGitHubRepo is not null)
            return;

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
            var deduplicated = DeduplicateRepositories(versionCounts);

            var bestChoice = deduplicated
                .OrderByDescending(x =>
                    x.Value.Any(y => container.Version?.IsSameVersionAs(y.TagName) == true)
                )
                .ThenByDescending(x => x.Value.Count)
                .First();

            container.GitHubRepo = bestChoice.Key;
            container.GitHubVersionRegex = bestChoice.Value.Any()
                ? VersionHelper.BuildRegexFromVersion(bestChoice.Value.First().TagName)
                : null;

            if (deduplicated.Count > 1)
            {
                container.SecondaryGitHubRepos = deduplicated
                    .Where(x => x.Key != bestChoice.Key)
                    .Select(x => x.Key)
                    .ToList();
            }
        }
    }
}
