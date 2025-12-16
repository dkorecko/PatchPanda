namespace PatchPanda.Web.Exceptions;

public class DockerCommandException(string command, int exitCode, string? stdOut, string? stdErr)
    : Exception($"Docker compose command {command} failed with exit code {exitCode}")
{
    public int ExitCode { get; } = exitCode;

    public string Command { get; } = command;

    public string? StdOut { get; } = stdOut;

    public string? StdErr { get; } = stdErr;
}
