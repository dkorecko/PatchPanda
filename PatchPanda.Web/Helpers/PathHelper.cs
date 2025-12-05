namespace PatchPanda.Web.Helpers;

public static class PathHelper
{
    public static string? ComputePathForEnvironment(this string? path)
    {
#if DEBUG
        return path;
#endif

        if (string.IsNullOrWhiteSpace(path))
            return null;

        string linuxPath = path.Replace('\\', '/');

        if (linuxPath.Length > 2 && linuxPath[1] == ':' && char.IsLetter(linuxPath[0]))
        {
            linuxPath = "/" + char.ToLowerInvariant(linuxPath[0]) + linuxPath[2..];
        }

        return linuxPath.TrimEnd('/');
    }
}
