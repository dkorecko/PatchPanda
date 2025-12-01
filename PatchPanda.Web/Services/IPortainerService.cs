namespace PatchPanda.Web.Services;

public interface IPortainerService
{
    bool IsConfigured { get; }

    Task<string?> GetStackFileContentAsync(string stackName);

    Task<bool> UpdateStackFileContentAsync(string stackName, string newFileContent);
}
