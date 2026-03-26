using System.Diagnostics;

namespace PatchPanda.Services;

public class HookService
{
    private readonly ILogger<HookService> _logger;

    public HookService(ILogger<HookService> logger) => _logger = logger;

    public async Task ExecuteHookAsync(string scriptPath, string composePath, string name, string oldVer, string newVer)
    {
        if (string.IsNullOrEmpty(scriptPath))
        {
            _logger.LogWarning("Hook script path is empty for {Name} ({ComposePath})", name, composePath);
            return;
        }

        if (!File.Exists(scriptPath))
        {
            _logger.LogError("Hook script not found: {ScriptPath} for {Name}", scriptPath, name);
            return;
        }

        _logger.LogInformation("Running post-update hook {ScriptPath} for {Name}", scriptPath, name);

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.EnvironmentVariables["PP_PROJECT_DIR"] = Path.GetDirectoryName(composePath) ?? string.Empty;
        startInfo.EnvironmentVariables["PP_NAME"] = name;
        startInfo.EnvironmentVariables["PP_OLD_VERSION"] = oldVer;
        startInfo.EnvironmentVariables["PP_NEW_VERSION"] = newVer;
        startInfo.EnvironmentVariables["PP_UPDATE_TIME"] = DateTime.UtcNow.ToString("o");

        using var process = new Process { StartInfo = startInfo };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        _logger.LogInformation("Hook stdout: {Stdout}", string.IsNullOrWhiteSpace(stdout) ? "(empty)" : stdout);

        if (!string.IsNullOrWhiteSpace(stderr))
            _logger.LogWarning("Hook stderr: {Stderr}", stderr);

        if (process.ExitCode != 0)
            _logger.LogError("Hook exited {ExitCode} for {ScriptPath}", process.ExitCode, scriptPath);
        else
            _logger.LogInformation("Hook completed successfully ({ScriptPath})", scriptPath);
    }
}