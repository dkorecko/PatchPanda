namespace PatchPanda.Units;

public class ParsingHelperTests
{
    [Fact]
    public void DeduplicateRepositories_RemovesDuplicateReposWithSameReleases()
    {
        var release1 = new Octokit.Release(
            url: "url1",
            htmlUrl: "htmlUrl1",
            assetsUrl: "assetsUrl1",
            uploadUrl: "uploadUrl1",
            id: 12345,
            nodeId: "nodeId1",
            tagName: "v1.0.0",
            targetCommitish: "main",
            name: "Release 1.0.0",
            body: "body",
            draft: false,
            prerelease: false,
            createdAt: DateTimeOffset.Now,
            publishedAt: DateTimeOffset.Now,
            author: null,
            tarballUrl: "tarball",
            zipballUrl: "zipball",
            assets: []
        );

        var release2 = new Octokit.Release(
            url: "url2",
            htmlUrl: "htmlUrl2",
            assetsUrl: "assetsUrl2",
            uploadUrl: "uploadUrl2",
            id: 12346,
            nodeId: "nodeId2",
            tagName: "v0.9.0",
            targetCommitish: "main",
            name: "Release 0.9.0",
            body: "body",
            draft: false,
            prerelease: false,
            createdAt: DateTimeOffset.Now.AddDays(-1),
            publishedAt: DateTimeOffset.Now.AddDays(-1),
            author: null,
            tarballUrl: "tarball",
            zipballUrl: "zipball",
            assets: []
        );

        IReadOnlyList<Octokit.Release> releases = new List<Octokit.Release> { release1, release2 };

        var repo1 = new Tuple<string, string>("linuxserver", "sonarr");
        var repo2 = new Tuple<string, string>("linuxserver", "docker-sonarr");

        var versionCounts = new Dictionary<Tuple<string, string>, IReadOnlyList<Octokit.Release>>
        {
            { repo1, releases },
            { repo2, releases }
        };

        var deduplicatedMethod = typeof(ParsingHelper)
            .GetMethod("DeduplicateRepositories", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(deduplicatedMethod);

        var result = (Dictionary<Tuple<string, string>, IReadOnlyList<Octokit.Release>>)deduplicatedMethod.Invoke(null, new object[] { versionCounts })!;

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.True(result.ContainsKey(repo2) || result.ContainsKey(repo1));
        
        var canonicalRepo = result.Keys.First();
        Assert.Equal("docker-sonarr", canonicalRepo.Item2);
    }

    [Fact]
    public void DeduplicateRepositories_PreservesNonDuplicateRepos()
    {
        var release1 = new Octokit.Release(
            url: "url1",
            htmlUrl: "htmlUrl1",
            assetsUrl: "assetsUrl1",
            uploadUrl: "uploadUrl1",
            id: 12345,
            nodeId: "nodeId1",
            tagName: "v1.0.0",
            targetCommitish: "main",
            name: "Release 1.0.0",
            body: "body",
            draft: false,
            prerelease: false,
            createdAt: DateTimeOffset.Now,
            publishedAt: DateTimeOffset.Now,
            author: null,
            tarballUrl: "tarball",
            zipballUrl: "zipball",
            assets: []
        );

        var release2 = new Octokit.Release(
            url: "url2",
            htmlUrl: "htmlUrl2",
            assetsUrl: "assetsUrl2",
            uploadUrl: "uploadUrl2",
            id: 99999,
            nodeId: "nodeId2",
            tagName: "v2.0.0",
            targetCommitish: "main",
            name: "Release 2.0.0",
            body: "body",
            draft: false,
            prerelease: false,
            createdAt: DateTimeOffset.Now,
            publishedAt: DateTimeOffset.Now,
            author: null,
            tarballUrl: "tarball",
            zipballUrl: "zipball",
            assets: []
        );

        IReadOnlyList<Octokit.Release> releases1 = new List<Octokit.Release> { release1 };
        IReadOnlyList<Octokit.Release> releases2 = new List<Octokit.Release> { release2 };

        var repo1 = new Tuple<string, string>("user1", "repo1");
        var repo2 = new Tuple<string, string>("user2", "repo2");

        var versionCounts = new Dictionary<Tuple<string, string>, IReadOnlyList<Octokit.Release>>
        {
            { repo1, releases1 },
            { repo2, releases2 }
        };

        var deduplicatedMethod = typeof(ParsingHelper)
            .GetMethod("DeduplicateRepositories", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(deduplicatedMethod);

        var result = (Dictionary<Tuple<string, string>, IReadOnlyList<Octokit.Release>>)deduplicatedMethod.Invoke(null, new object[] { versionCounts })!;

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey(repo1));
        Assert.True(result.ContainsKey(repo2));
    }
}
