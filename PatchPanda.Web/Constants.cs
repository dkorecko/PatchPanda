namespace PatchPanda.Web;

public static class Constants
{
#if DEBUG
    public const string APP_NAME = "PatchPanda [DEV]";
#else
    public const string APP_NAME = "PatchPanda";
#endif
}
