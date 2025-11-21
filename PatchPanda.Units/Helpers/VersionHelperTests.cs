namespace PatchPanda.Units.Helpers;

public class VersionHelperTests
{
    [Fact]
    public void IsSameVersionAs_IgnoresVPrefix()
    {
        Assert.True("v1.2.3".IsSameVersionAs("1.2.3"));
    }

    [Fact]
    public void IsSameVersionAs_DetectsDifferentVersions()
    {
        Assert.False("v1.2.3".IsSameVersionAs("v1.2.4"));
    }

    [Fact]
    public void IsNewerThan_DetectsNewerVersion()
    {
        Assert.True("v1.2.4".IsNewerThan("v1.2.3"));
        Assert.False("v1.2.3".IsNewerThan("v1.2.3"));
    }

    [Fact]
    public void IsNewerThan_HandlesAtStyleVersions()
    {
        Assert.True("n8n@1.119.2".IsNewerThan("1.118.1"));
    }

    [Fact]
    public void NewerComparison_OrdersCorrectly()
    {
        var result = VersionHelper.NewerComparison("v1.2.4", "v1.2.3");
        Assert.Equal(-1, result);
    }

    [Fact]
    public void BuildRegexFromVersion_MatchesExpectedPattern()
    {
        var regex = VersionHelper.BuildRegexFromVersion("v1.2.3");

        var re = new Regex(regex);

        Assert.Matches(re, "v1.2.3");
        Assert.DoesNotMatch(re, "v1.2.3-alpha");
    }

    [Fact]
    public void IsNewerThan_HandlesLsSuffix()
    {
        Assert.True("v1.5.3-ls325".IsNewerThan("v1.5.3-ls324"));
        Assert.False("v1.5.3-ls324".IsNewerThan("v1.5.3-ls325"));
    }

    [Fact]
    public void BuildRegexFromVersion_HandlesLsAndRsuffix()
    {
        var regex1 = VersionHelper.BuildRegexFromVersion("v1.5.3-ls325");
        var re1 = new Regex(regex1);
        Assert.Matches(re1, "v1.5.3-ls325");

        var regex2 = VersionHelper.BuildRegexFromVersion("v1.2.3-r123");
        var re2 = new Regex(regex2);
        Assert.Matches(re2, "v1.2.3-r123");
    }

    [Fact]
    public void IsSameVersionAs_IgnoresExtraNumericSegments()
    {
        Assert.True("v1.2.3.0".IsSameVersionAs("1.2.3.0"));
        Assert.False("v1.2.3.1".IsSameVersionAs("1.2.3.0"));
    }

    [Fact]
    public void IsNewerThan_HandlesMoreThanThreeSegments()
    {
        Assert.True("v1.2.3.4".IsNewerThan("v1.2.3.3"));
        Assert.False("v1.2.3.4".IsNewerThan("v1.2.4.0"));
    }
}
