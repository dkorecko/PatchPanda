using System.Text.RegularExpressions;

namespace PatchPanda.Web.Services;

public class VersionService
{
    private GitHubClient GetClient() => new(new ProductHeaderValue("PatchPanda"));

    public async Task<IEnumerable<string>> GetNewerVersions(ComposeApp app)
    {
        var client = GetClient();
        var (owner, repo) = GetOwnerRepoName(app.GitHubRepo);
        var allReleases = (
            await client.Repository.Release.GetAll(owner, repo, new ApiOptions { PageSize = 100 })
        );

        var validReleases = allReleases.Where(x =>
            Regex.IsMatch(x.TagName, app.Regex) || Regex.IsMatch(x.Name, app.Regex)
        );

        var newerVersions = validReleases
            .Where(x => x.TagName.IsNewerThan(app.Version))
            .Select(x => x.TagName);

        Constants
            .COMPOSE_APPS!.SelectMany(x => x.Apps)
            .First(x => x.Name == app.Name)
            .NewerVersions = newerVersions;

        return newerVersions;
    }

    public Tuple<string, string> GetOwnerRepoName(string url)
    {
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length >= 2)
        {
            return Tuple.Create(segments[0], segments[1]);
        }
        throw new ArgumentException("Invalid GitHub repository URL");
    }
}
