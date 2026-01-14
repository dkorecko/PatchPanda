namespace PatchPanda.Web.Services.Interfaces;

public interface IPortainerService
{
    bool IsConfigured { get; }

    Task<string?> GetStackFileContentAsync(string stackName);

    Task<bool> UpdateStackFileContentAsync(string stackName, string newFileContent);
}
