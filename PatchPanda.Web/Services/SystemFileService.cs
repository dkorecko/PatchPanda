namespace PatchPanda.Web.Services;

public class SystemFileService : IFileService
{
    public virtual bool Exists(string? path) => File.Exists(path);

    public virtual string ReadAllText(string path) => File.ReadAllText(path);

    public virtual void WriteAllText(string path, string? contents)
    {
        File.WriteAllText(path, contents);
    }
}
