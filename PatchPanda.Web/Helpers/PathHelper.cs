namespace PatchPanda.Web.Helpers;

public static class PathHelper
{
    public static string? ComputePathForEnvironment(this string? path, IFileService fileService)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (fileService.Exists(path))
            return path;

        string? resultPath;
        if (path.Contains(':'))
        {
            resultPath = path.Replace('\\', '/');

            if (resultPath.Length > 2 && resultPath[1] == ':' && char.IsLetter(resultPath[0]))
            {
                resultPath = "/" + char.ToLowerInvariant(resultPath[0]) + resultPath[2..];
            }

            resultPath = resultPath.TrimEnd('/');
        }
        else
        {
            resultPath = path.TrimStart('/').Replace('/', '\\');
            if (resultPath.Length > 2 && resultPath[1] == '\\' && char.IsLetter(resultPath[0]))
            {
                resultPath = char.ToUpperInvariant(resultPath[0]) + ":" + resultPath[1..];
            }
            resultPath = resultPath.TrimEnd('\\');

            if (fileService.Exists(resultPath))
                return resultPath;
        }

        if (fileService.Exists(resultPath))
            return resultPath;

        return null;
    }
}
