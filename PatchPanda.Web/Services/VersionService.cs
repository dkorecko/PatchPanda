using System.Text.RegularExpressions;

namespace PatchPanda.Web.Services;

public class VersionService
{
    private readonly ILogger<VersionService> _logger;
    private readonly IConfiguration _configuration;

    private string? Username { get; init; }
    private string? Password { get; init; }

    public VersionService(ILogger<VersionService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        Username = _configuration["GITHUB_USERNAME"];
        Password = _configuration["GITHUB_PASSWORD"];

        if (Username is null || Password is null)
            _logger.LogWarning(
                "GitHub credentials are not set in environment variables. You may run into rate limiting issues."
            );
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

    public async Task<IEnumerable<AppVersion>> GetNewerVersions(ComposeApp app)
    {
        if (app.GitHubRepo is null || app.Version is null || app.Regex is null)
            return [];

        _logger.LogInformation(
            "Going to initiate request to get newer versions for app {AppName}",
            app.Name
        );

        var client = GetClient();

        var apiInfo = client.GetLastApiInfo();

        if (apiInfo is not null && apiInfo.RateLimit.Remaining == 0)
            throw new RateLimitException(apiInfo.RateLimit.Reset, apiInfo.RateLimit.Limit);

        try
        {
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

            SetNewerVersions(app, newerVersions);

            _logger.LogInformation(
                "Got {Count} newer versions, newest is {Newest}. Looked for regex {Regex}, received {ValidReleaseCount} valid releases from GitHub, example tag name {TagName} and name {Name} of release.",
                newerVersions.Count(),
                newerVersions.FirstOrDefault()?.VersionNumber ?? "None found",
                app.Regex,
                validReleases.Count(),
                validReleases.FirstOrDefault()?.TagName ?? "N/A",
                validReleases.FirstOrDefault()?.Name ?? "N/A"
            );

            return newerVersions;
        }
        catch (RateLimitExceededException ex)
        {
            throw new RateLimitException(ex.Reset, ex.Limit);
        }
    }

    public void SetNewerVersions(ComposeApp app, IEnumerable<AppVersion> newerVersions)
    {
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
