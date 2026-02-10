using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;
using PatchPanda.Units.Helpers;
using PatchPanda.Web;
using PatchPanda.Web.Db;
using PatchPanda.Web.Services;

namespace PatchPanda.Units.Services;

public class VersionServiceTests
{
    private class TestableVersionService : VersionService
    {
        public Mock<IGitHubClient> MockGitHubClient { get; } = new();

        public TestableVersionService(
            ILogger<VersionService> logger,
            IConfiguration configuration,
            IDbContextFactory<DataContext> dbContextFactory,
            IAiService aiService
        ) : base(logger, configuration, dbContextFactory, aiService)
        {
        }

        protected override IGitHubClient GetClient()
        {
            return MockGitHubClient.Object;
        }
    }

    private readonly Mock<ILogger<VersionService>> _logger = new();
    private readonly Mock<IConfiguration> _configuration = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly IDbContextFactory<DataContext> _dbContextFactory = Helper.CreateInMemoryFactory();

    [Fact]
    public async Task SecurityCheck_Retries_On_Failure()
    {
        // Arrange
        var service = new TestableVersionService(
            _logger.Object,
            _configuration.Object,
            _dbContextFactory,
            _aiService.Object
        );

        using var db = _dbContextFactory.CreateDbContext();
        
        // Setup App Settings
        db.AppSettings.Add(new AppSetting { Key = Constants.SettingsKeys.SECURITY_SCANNING_ENABLED, Value = "true" });
        await db.SaveChangesAsync();

        var stack = Helper.GetTestStack();
        var app = stack.Apps[0];
        app.GitHubRepo = new Tuple<string, string>(TestData.GITHUB_OWNER, TestData.GITHUB_REPO);
        app.NewerVersions.Clear();
        
        db.Containers.Add(app);
        await db.SaveChangesAsync();

        // Setup AI Service
        _aiService.Setup(x => x.IsInitialized()).Returns(true);
        
        // Fail twice, then succeed
        _aiService.SetupSequence(x => x.AnalyzeDiff(It.IsAny<string>()))
            .ThrowsAsync(new Exception(TestData.AI_ERROR))
            .ThrowsAsync(new Exception(TestData.AI_ERROR))
            .ReturnsAsync(new SecurityAnalysisResult { Analysis = TestData.SAFE_ANALYSIS, IsSuspectedMalicious = false });

        // Setup GitHub Client
        var mockRepo = new Mock<IRepositoriesClient>();
        var mockRelease = new Mock<IReleasesClient>();
        var mockCommit = new Mock<IRepositoryCommitsClient>();
        
        service.MockGitHubClient.Setup(x => x.Repository).Returns(mockRepo.Object);
        mockRepo.Setup(x => x.Release).Returns(mockRelease.Object);
        mockRepo.Setup(x => x.Commit).Returns(mockCommit.Object);

        var releaseNew = new Release(
            TestData.DUMMY_URL, TestData.DUMMY_URL, TestData.DUMMY_URL, TestData.DUMMY_URL, 1, "nodeId", TestData.NEW_VERSION,
            "master", TestData.NEW_VERSION, TestData.BODY, false, false, DateTimeOffset.Now, 
            DateTimeOffset.Now, new Author(), TestData.DUMMY_URL, TestData.DUMMY_URL, null
        );

        var releaseCurrent = new Release(
            TestData.DUMMY_URL, TestData.DUMMY_URL, TestData.DUMMY_URL, TestData.DUMMY_URL, 2, "nodeId", TestData.VERSION,
            "master", TestData.VERSION, TestData.BODY, false, false, DateTimeOffset.Now, 
            DateTimeOffset.Now, new Author(), TestData.DUMMY_URL, TestData.DUMMY_URL, null
        );

        mockRelease.Setup(x => x.GetAll(TestData.GITHUB_OWNER, TestData.GITHUB_REPO, It.IsAny<ApiOptions>()))
            .ReturnsAsync(new List<Release> { releaseNew, releaseCurrent });
            
        // Construct GitHubCommitFile with necessary patch content
        // GitHubCommitFile(filename, additions, deletions, changes, status, blobUrl, contentsUrl, rawUrl, sha, patch, previousFileName)
        var commitFile = new GitHubCommitFile("filename", 0, 0, 0, "status", TestData.DUMMY_URL, TestData.DUMMY_URL, TestData.DUMMY_URL, "sha", TestData.PATCH_CONTENT, null);
        
        // Construct CompareResult
        // CompareResult(url, htmlUrl, permalinkUrl, diffUrl, patchUrl, baseCommit, mergeBaseCommit, status, aheadBy, behindBy, totalCommits, commits, files)
        var compareResult = new CompareResult(
            TestData.DUMMY_URL, TestData.DUMMY_URL, TestData.DUMMY_URL, TestData.DUMMY_URL, TestData.DUMMY_URL, 
            null, // baseCommit
            null, // mergeBaseCommit
            "ahead", // status
            1, // aheadBy
            0, // behindBy
            1, // totalCommits
            new List<GitHubCommit>(), // commits
            new List<GitHubCommitFile> { commitFile } // files
        );

        mockCommit.Setup(x => x.Compare(TestData.GITHUB_OWNER, TestData.GITHUB_REPO, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(compareResult);

        // Act
        var result = await service.GetNewerVersions(app, []);

        // Assert
        _aiService.Verify(x => x.AnalyzeDiff(It.IsAny<string>()), Times.AtLeast(3));
        Assert.Single(result);
        Assert.Equal(TestData.SAFE_ANALYSIS, result.First().SecurityAnalysis);
    }
}
