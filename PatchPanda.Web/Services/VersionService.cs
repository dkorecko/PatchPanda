using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PatchPanda.Web.Services;

public class VersionService
{
    private readonly ILogger<VersionService> _logger;

    public VersionService(ILogger<VersionService> logger)
    {
        _logger = logger;
    }

    private GitHubClient GetClient() => new(new ProductHeaderValue("PatchPanda"));

    public async Task<IEnumerable<AppVersion>> GetNewerVersions(ComposeApp app)
    {
        if (app.GitHubRepo is null)
            return [];

        var client = GetClient();

        var apiInfo = client.GetLastApiInfo();

        if (apiInfo is not null && apiInfo.RateLimit.Remaining == 0)
            throw new RateLimitException(apiInfo.RateLimit.Reset);

        var (owner, repo) = GetOwnerRepoName(app.GitHubRepo);
        var allReleases = (
            await client.Repository.Release.GetAll(
                owner,
                repo,
                new ApiOptions { PageSize = 100, PageCount = 1 }
            )
        );

        var validReleases = allReleases.Where(x =>
            Regex.IsMatch(x.TagName, app.Regex) || Regex.IsMatch(x.Name, app.Regex)
        );

        var newerVersions = validReleases
            .Where(x => x.TagName.IsNewerThan(app.Version))
            .Select(x => new AppVersion()
            {
                Body = x.Body,
                Name = x.Name,
                Prerelease = x.Prerelease,
                VersionNumber = x.TagName,
                Breaking = x.Body.Has("breaking") || x.Body.Has("critical")
            });

        var targetApp = Constants
            .COMPOSE_APPS!.SelectMany(x => x.Apps)
            .First(x => x.Name == app.Name);

        var alreadyNotified = targetApp
            .NewerVersions.Where(x => x.Notified)
            .Select(x => x.VersionNumber)
            .ToHashSet();

        targetApp.NewerVersions = newerVersions;

        foreach (var version in targetApp.NewerVersions)
        {
            if (alreadyNotified.Contains(version.VersionNumber))
            {
                version.Notified = true;
            }
        }

        return newerVersions;
    }

    public Tuple<string, string> GetOwnerRepoName(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 2)
            {
                return Tuple.Create(segments[0], segments[1]);
            }
            else
                throw new ArgumentException("Invalid GitHub repository URL");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing GitHub repository URL: {Url}", url);
            throw;
        }
    }
}
