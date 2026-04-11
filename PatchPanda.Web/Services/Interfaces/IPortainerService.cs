namespace PatchPanda.Web.Services.Interfaces;

public interface IPortainerService
{
    bool IsConfigured { get; }

    bool IsAccessTokenConfigured { get; }

    Task<bool> ValidateAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<string?> GetStackFileContentAsync(
        string stackName,
        CancellationToken cancellationToken = default
    );

    Task UpdateStackFileContentAsync(
        string stackName,
        string newFileContent,
        CancellationToken cancellationToken = default
    );
}
