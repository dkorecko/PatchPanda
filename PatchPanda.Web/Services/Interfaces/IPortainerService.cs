namespace PatchPanda.Web.Services.Interfaces;

public interface IPortainerService
{
    bool IsConfigured { get; }

    bool IsAccessTokenConfigured { get; }

    Task<bool> ValidateAccessTokenAsync();

    Task<string?> GetStackFileContentAsync(string stackName);

    Task UpdateStackFileContentAsync(string stackName, string newFileContent);
}
