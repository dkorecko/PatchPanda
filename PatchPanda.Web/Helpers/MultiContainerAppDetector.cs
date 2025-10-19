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
    }
}
