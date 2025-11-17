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

    public async Task<IReadOnlyList<Release>> GetVersions(Tuple<string, string> repo)
    {
        _logger.LogInformation(
            "Going to initiate request to get newer versions for {RepoOwner}/{RepoName}",
            repo.Item1,
            repo.Item2
        );

        var client = GetClient();

        var apiInfo = client.GetLastApiInfo();

        if (apiInfo is not null && apiInfo.RateLimit.Remaining == 0)
            throw new RateLimitException(apiInfo.RateLimit.Reset, apiInfo.RateLimit.Limit);

        try
        {
            var allReleases = (
                await client.Repository.Release.GetAll(
                    repo.Item1,
                    repo.Item2,
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
        var repo = app.GetGitHubRepo();

        if (repo is null || app.Version is null || app.GitHubVersionRegex is null)
            return [];

        var allReleases = await GetVersions(repo);

        var validReleases = allReleases.Where(x =>
            (x.TagName is not null && Regex.IsMatch(x.TagName, app.GitHubVersionRegex))
            || (x.Name is not null && Regex.IsMatch(x.Name, app.GitHubVersionRegex))
        );

        using var db = _dbContextFactory.CreateDbContext();

        List<Container> allApps = [app, .. otherApps];

        var targetApps = await db
            .Containers.Where(x => allApps.Select(y => y.Id).Contains(x.Id))
            .ToListAsync();

        List<Release> additionalReleases = [];

        if (app.SecondaryGitHubRepos is not null && app.SecondaryGitHubRepos.Count != 0)
        {
            foreach (var secondaryRepo in app.SecondaryGitHubRepos)
            {
                var versions = await GetVersions(secondaryRepo);

                if (versions.Any())
                    additionalReleases.AddRange(versions);
            }
        }

        var newerVersions = validReleases
            .Where(x => x.TagName is not null && x.TagName.IsNewerThan(app.Version))
            .Select(x => new AppVersion()
            {
                Body = x.Body,
                Name = x.Name,
                Prerelease = x.Prerelease,
                VersionNumber = x.TagName,
                Breaking = false,
                Applications = targetApps
            });

        var appNewerVersions = await db
            .AppVersions.Include(x => x.Applications)
            .Where(av => av.Applications.Any(a => a.Id == app.Id))
            .ToListAsync();

        var notSeenNewVersions = newerVersions
            .Where(nv => !appNewerVersions.Any(av => av.VersionNumber == nv.VersionNumber))
            .ToList();

        notSeenNewVersions.Sort(
            (a, b) => VersionHelper.NewerComparison(a.VersionNumber, b.VersionNumber)
        );

        UpdateBodiesWithSecondaryReleaseNotes(notSeenNewVersions, app, additionalReleases);

        notSeenNewVersions.ForEach(x =>
        {
            if (
                x.Body.Has("breaking")
                || x.Body.Has("critical")
                || x.Body.Has("review before")
                || x.Body.Has("before upgrad")
                || x.Body.Has("important")
                || x.Body.Contains("Warning")
            )
                x.Breaking = true;
        });

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

    public void UpdateBodiesWithSecondaryReleaseNotes(
        List<AppVersion> newVersions,
        Container app,
        List<Release> secondaryReleases
    )
    {
        ArgumentNullException.ThrowIfNull(app.Version);

        var remainingSecondaryReleases = secondaryReleases.ToList();

        remainingSecondaryReleases.Sort(
            (a, b) => VersionHelper.NewerComparison(a.TagName, b.TagName)
        );

        var matchingCurrentAdditionalRelease = remainingSecondaryReleases.FirstOrDefault(ar =>
            ar.TagName.TrimStart('v') == app.Version.Split("-ls")[0].TrimStart('v')
        );

        if (matchingCurrentAdditionalRelease is null)
            return;

        remainingSecondaryReleases = remainingSecondaryReleases
            .Take(remainingSecondaryReleases.IndexOf(matchingCurrentAdditionalRelease))
            .ToList();

        newVersions.Reverse();

        foreach (var newVersion in newVersions)
        {
            if (!newVersion.VersionNumber.Contains("-ls"))
                continue;

            var matchingAdditionalRelease = remainingSecondaryReleases.FirstOrDefault(ar =>
                ar.TagName.TrimStart('v') == newVersion.VersionNumber.Split("-ls")[0].TrimStart('v')
            );

            if (matchingAdditionalRelease is null)
                continue;

            var earlierRelevantAdditionalReleases = remainingSecondaryReleases
                .Where(x => matchingAdditionalRelease.TagName.IsNewerThan(x.TagName))
                .ToList();

            List<Release> allRelevantReleases =
            [
                matchingAdditionalRelease,
                .. earlierRelevantAdditionalReleases
            ];

            if (allRelevantReleases.Any())
            {
                newVersion.Body +=
                    $"\n\n## 📜 Additional Release Notes - {newVersion.VersionNumber}\n";
                foreach (var rel in allRelevantReleases)
                {
                    newVersion.Body += $"\n---\n### __{rel.Name}__\n{rel.Body}\n";
                }
            }
        }

        newVersions.Reverse();
    }
}
