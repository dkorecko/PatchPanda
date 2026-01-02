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

    public static class Cascading
    {
        public const string TOASTS = "TOASTS";
    }

    public static class VariableKeys
    {
        public const string APPRISE_NOTIFICATION_URLS = "APPRISE_NOTIFICATION_URLS";
        public const string APPRISE_API_URL = "APPRISE_API_URL";
        public const string DISCORD_WEBHOOK_URL = "DISCORD_WEBHOOK_URL";
        public const string BASE_URL = "BASE_URL";
        public const string PORTAINER_URL = "PORTAINER_URL";
        public const string PORTAINER_USERNAME = "PORTAINER_USERNAME";
        public const string PORTAINER_PASSWORD = "PORTAINER_PASSWORD";
        public const string OLLAMA_URL = "OLLAMA_URL";
        public const string OLLAMA_MODEL = "OLLAMA_MODEL";
        public const string OLLAMA_NUM_CTX = "OLLAMA_NUM_CTX";
        public const string APP_VERSION = "APP_VERSION";
    }

    public static class SettingsKeys
    {
        public const string AUTO_UPDATE_ENABLED = "AUTO_UPDATE_ENABLED";
        public const string AUTO_UPDATE_DELAY_HOURS = "AUTO_UPDATE_DELAY_HOURS";
        public const string SECURITY_SCANNING_ENABLED = "SECURITY_SCANNING_ENABLED";
    }

    public static class Limits
    {
        public const int MAX_OLLAMA_ATTEMPTS = 3;
        public const int MINIMUM_UPDATE_STEPS = 3;
    }
}
