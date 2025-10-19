namespace PatchPanda.Web.Helpers;

public class MultiContainerAppDetector
{
    public static void FillMultiContainerApps(ComposeStack stack)
    {
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

            stack.MultiContainerApps.Add(group.Key);
            foreach (var app in group)
                app.FromMultiContainer = group.Key;
        }

        stack
            .Apps.Where(x => x.FromMultiContainer is null)
            .GroupBy(app => app.GitHubRepo)
            .Where(g => g.Count() > 1 && g.Key is not null)
            .ToList()
            .ForEach(group =>
            {
                var multiContainerName = group.Key!.Split('/').Last();
                stack.MultiContainerApps.Add(multiContainerName);
                foreach (var app in group)
                    app.FromMultiContainer = multiContainerName;
            });

        var remainingApps = stack.Apps.Where(x => x.FromMultiContainer is null);

        foreach (var remainingApp in remainingApps)
        {
            var otherApps = remainingApps.Except([remainingApp]);

            var matchingApps = otherApps.Where(x =>
                x.Name.StartsWith($"{remainingApp.Name}-")
                || x.Name.StartsWith($"{remainingApp.Name}_")
            );

            stack.MultiContainerApps.Add(remainingApp.Name);
            foreach (var app in matchingApps)
                app.FromMultiContainer = remainingApp.Name;
        }
    }
}
