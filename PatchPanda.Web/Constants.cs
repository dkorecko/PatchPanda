namespace PatchPanda.Web;

public static class Constants
{
    public static string? BASE_URL = null;
#if DEBUG
    public const string APP_NAME = "PatchPanda [DEV]";
#else
    public const string APP_NAME = "PatchPanda";
#endif
    public const string DB_NAME = "patchpanda.db";
}
