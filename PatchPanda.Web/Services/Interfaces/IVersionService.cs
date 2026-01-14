using Octokit;

namespace PatchPanda.Web.Services.Interfaces;

public interface IVersionService
{
    Task<IReadOnlyList<Release>> GetVersions(Tuple<string, string> repo);

    Task<IEnumerable<AppVersion>> GetNewerVersions(Container app, Container[] otherApps);

    void UpdateBodiesWithSecondaryReleaseNotes(
        List<AppVersion> newVersions,
        Container app,
        List<Release> secondaryReleases
    );
}
