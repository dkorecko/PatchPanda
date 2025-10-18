namespace PatchPanda.Web.Services;

public class VersionService
{
    private GitHubClient GetClient() => new GitHubClient(new ProductHeaderValue("PatchPanda"));

    public async Task<IEnumerable<string>> GetNewerVersions(string url, string previousVersion)
    {
        var client = GetClient();
        var (owner, repo) = GetOwnerRepoName(url);
        var releases = await client.Repository.Release.GetAll(
            owner,
            repo,
            new ApiOptions { PageSize = 100 }
        );
        return releases.Select(x => x.Name);
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
