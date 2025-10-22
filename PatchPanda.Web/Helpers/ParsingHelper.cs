using System.Text.Json;
using System.Text.RegularExpressions;

namespace PatchPanda.Web.Helpers;

public static class ParsingHelper
{
    public static async Task SetGitHubRepo(
        this Container container,
        ContainerListResponse response,
        VersionService versionService
    )
    {
        List<string> repos = [];

        var fullResponse = JsonSerializer.Serialize(response);

        var matches = Regex
            .Matches(fullResponse, @"https://github.com\/[a-zA-Z0-9-]+\/[a-zA-Z0-9-]+")
            .Select(x => x.Value)
            .Distinct();

        foreach (var match in matches)
        {
            container.GitHubRepo = match;
            var versions = await versionService.GetNewerVersions(container, []);

            if (versions.Any())
                return;
        }
    }
}
