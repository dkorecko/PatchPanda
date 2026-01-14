namespace PatchPanda.Web.Services.Interfaces;

public interface IAppriseService
{
    public bool IsInitialized { get; }

    public IReadOnlyList<string> GetEndpoints();

    public Task SendAsync(string message, CancellationToken cancellationToken = default);
}
