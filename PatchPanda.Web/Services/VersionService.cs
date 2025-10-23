using System.Text.RegularExpressions;
using Octokit;

namespace PatchPanda.Web.Services;

public class VersionService
{
    private readonly ILogger<VersionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<DataContext> _dbContextFactory;

    private string? Username { get; init; }
    private string? Password { get; init; }

    public VersionService(
        ILogger<VersionService> logger,
        IConfiguration configuration,
        IDbContextFactory<DataContext> dbContextFactory
    )
    {
        _logger = logger;
        _configuration = configuration;

        Username = _configuration["GITHUB_USERNAME"];
        Password = _configuration["GITHUB_PASSWORD"];

        if (Username is null || Password is null)
            _logger.LogWarning(
                "GitHub credentials are not set in environment variables. You may run into rate limiting issues."
            );

        _dbContextFactory = dbContextFactory;
    }

    private GitHubClient GetClient()
    {
        var client = new GitHubClient(new ProductHeaderValue("PatchPanda"));

        if (Username is not null && Password is not null)
        {
            var tokenAuth = new Credentials(Username, Password);
            client.Credentials = tokenAuth;
        }

        return client;
    }

    public async Task<IReadOnlyList<Release>> GetVersions(string repoUrl)
    {
        _logger.LogInformation(
            "Going to initiate request to get newer versions for repo {RepoUrl}",
            repoUrl
        );

        var client = GetClient();

        var apiInfo = client.GetLastApiInfo();

        if (apiInfo is not null && apiInfo.RateLimit.Remaining == 0)
            throw new RateLimitException(apiInfo.RateLimit.Reset, apiInfo.RateLimit.Limit);

        try
        {
            var (owner, repo) = GetOwnerRepoName(repoUrl);
            var allReleases = (
                await client.Repository.Release.GetAll(
                    owner,
                    repo,
                    new ApiOptions { PageSize = 100, PageCount = 1 }
                )
            );

            _logger.LogInformation("Got {Count} releases.", allReleases.Count);

            return allReleases;
        }
        catch (RateLimitExceededException ex)
        {
            throw new RateLimitException(ex.Reset, ex.Limit);
        }
    }

    public async Task<IEnumerable<AppVersion>> GetNewerVersions(
        Container app,
        Container[] otherApps
    )
    {
        if (app.GitHubRepo is null || app.Version is null || app.Regex is null)
            return [];

        var allReleases = await GetVersions(app.GitHubRepo);

        var validReleases = allReleases.Where(x =>
            Regex.IsMatch(x.TagName, app.Regex) || Regex.IsMatch(x.Name, app.Regex)
        );

        using var db = _dbContextFactory.CreateDbContext();

        List<Container> allApps = [app, .. otherApps];

        var targetApps = await db
            .Containers.Where(x => allApps.Select(y => y.Id).Contains(x.Id))
            .ToListAsync();

        var newerVersions = validReleases
            .Where(x => x.TagName.IsNewerThan(app.Version))
            .Select(x => new AppVersion()
            {
                Body = x.Body,
                Name = x.Name,
                Prerelease = x.Prerelease,
                VersionNumber = x.TagName,
                Breaking =
                    x.Body.Has("breaking")
                    || x.Body.Has("critical")
                    || x.Body.Has("review before")
                    || x.Body.Has("before upgrad")
                    || x.Body.Has("important")
                    || x.Body.Has("warning"),
                Applications = targetApps
            });

        var appNewerVersions = await db
            .AppVersions.Include(x => x.Applications)
            .Where(av => av.Applications.Any(a => a.Id == app.Id))
            .ToListAsync();

        var notSeenNewVersions = newerVersions
            .Where(nv => !appNewerVersions.Any(av => av.VersionNumber == nv.VersionNumber))
            .ToList();

        db.AppVersions.AddRange(notSeenNewVersions);

        targetApps.ForEach(a => a.LastVersionCheck = DateTime.Now);

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Got {Count} newer versions, newest is {Newest}. Looked for regex {Regex}, received {ValidReleaseCount} valid releases from GitHub, example tag name {TagName} and name {Name} of release.",
            newerVersions.Count(),
            newerVersions.FirstOrDefault()?.VersionNumber ?? "None found",
            app.Regex,
            validReleases.Count(),
            validReleases.FirstOrDefault()?.TagName ?? "N/A",
            validReleases.FirstOrDefault()?.Name ?? "N/A"
        );

        return notSeenNewVersions;
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
