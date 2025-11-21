namespace PatchPanda.Units;

public static class TestData
{
    public const string IMAGE = "example/image:v1.0.0";
    public const string IMAGE_NEW_VERSION = "example/image:v1.1.0";

    public const string VERSION = "v1.0.0";
    public const string NEW_VERSION = "v1.1.0";

    public const string REGEX = "^v\\d+\\.\\d+\\.\\d+$";
    public const string SHA = "sha256:blabla";

    public const string UPTIME = "Up 1 hour";

    public const string GITHUB_OWNER = "some";
    public const string GITHUB_REPO = "repo";
    public const string GITHUB_URL = "https://github.com/some/repo";
    public const string RELEASE_TAG = "v1.2.3";

    public const string OWNER_A = "ownera";
    public const string REPO_A = "repoa";
    public const string URL_A = "https://github.com/ownera/repoa";
    public const string OWNER_B = "ownerb";
    public const string REPO_B = "repob";
    public const string URL_B = "https://github.com/ownerb/repob";
    public const string MULTI_IMAGE = "ghcr.io/ownera/repoa:1.0.0,https://github.com/ownerb/repob";
    public const string RELEASE_TAG_A = "v1.0.0";
    public const string RELEASE_TAG_B = "v2.0.0";
    public const string ALPINE_IMAGE = "alpine:3.16";
}
