namespace PatchPanda.Web.Services;

public class FileService
{
    public const string ROOT_DIR = @"C:\Users\PC\Coding\self-host";

    public FileService()
    {
        if (!Directory.Exists(ROOT_DIR))
            throw new Exception("Directory for compose files not provided or provided incorrectly");
    }

    public List<string> GetAllComposePaths(string currentPath, int level)
    {
        if (level > 4)
            return [];

        List<string> results = [];

        foreach(var file in Directory.GetFiles(currentPath))
        {
            if (file.EndsWith("compose.yaml") || file.EndsWith("compose.yml"))
            {
                results.Add(file);
                return results;
            }
        }

        foreach(var newPath in Directory.GetDirectories(currentPath))
        {
            results.AddRange(GetAllComposePaths(newPath, level++));
        }

        return results;
    }
}
