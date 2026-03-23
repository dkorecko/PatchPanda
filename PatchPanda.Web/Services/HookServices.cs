using System.Diagnostics;

namespace PatchPanda.Services;

public class HookService
{
    private readonly ILogger<HookService> _logger;

    public HookService(ILogger<HookService> logger) => _logger = logger;

    public async Task ExecuteHookAsync(string scriptPath, string composePath, string name, string oldVer, string newVer)
    {
        if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath)) return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Exporting your requested variables
        startInfo.EnvironmentVariables["PP_PROJECT_DIR"] = Path.GetDirectoryName(composePath) ?? string.Empty;
        startInfo.EnvironmentVariables["PP_NAME"] = name;
        startInfo.EnvironmentVariables["PP_OLD_VERSION"] = oldVer;
        startInfo.EnvironmentVariables["PP_NEW_VERSION"] = newVer;
        startInfo.EnvironmentVariables["PP_UPDATE_TIME"] = DateTime.UtcNow.ToString("o");

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync();
    }
}