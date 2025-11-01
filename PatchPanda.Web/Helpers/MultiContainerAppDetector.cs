namespace PatchPanda.Web.Helpers;

public class MultiContainerAppDetector
{
    public static void FillMultiContainerApps(ComposeStack stack, DataContext db)
    {
        var remainingApps = stack.Apps.ToList();

        foreach (
            var group in stack.Apps.GroupBy(app =>
                app.Name.Contains('-')
                    ? app.Name.Split('-')[0]
                    : (app.Name.Contains('_') ? app.Name.Split('_')[0] : null)
            )
        )
        {
            if (group.Key is null || group.Count() < 2)
                continue;

            var multiContainerApp = new MultiContainerApp
            {
                AppName = group.Key,
                Containers = group.ToList()
            };

            db.MultiContainerApps.Add(multiContainerApp);
        }

        remainingApps
            .GroupBy(app => app.GetGitHubRepo())
            .Where(g => g.Count() > 1 && g.Key is not null)
            .ToList()
            .ForEach(group =>
            {
                var multiContainerName = group.Key!.Item2;

                var multiContainerApp = new MultiContainerApp
                {
                    AppName = multiContainerName,
                    Containers = [.. group]
                };

                db.MultiContainerApps.Add(multiContainerApp);
            });

        foreach (var remainingApp in remainingApps)
        {
            var otherApps = remainingApps.Except([remainingApp]);

            var matchingApps = otherApps.Where(x =>
                x.Name.StartsWith($"{remainingApp.Name}-")
                || x.Name.StartsWith($"{remainingApp.Name}_")
            );

            if (!matchingApps.Any())
                continue;

            var multiContainerApp = new MultiContainerApp
            {
                AppName = remainingApp.Name,
                Containers = [remainingApp, .. matchingApps]
            };

            db.MultiContainerApps.Add(multiContainerApp);
        }
    }
}
