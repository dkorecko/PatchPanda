namespace PatchPanda.Web.Helpers;

public static class VersionHelper
{
    public static string BuildRegexFromVersion(string version)
    {
        string regex = "^";

        if (version.StartsWith('v'))
            regex += "v";

        string cleanedVersion = version.TrimStart('v');
        var periodSplit = cleanedVersion.Split('.');

        List<string> digitRegexes = [];
        for (int i = 0; i < periodSplit.Length; i++)
        {
            digitRegexes.Add("\\d+");
        }

        regex += string.Join('.', digitRegexes);

        var dashSplit = periodSplit[^1].Split('-');

        foreach (var dash in dashSplit[1..])
        {
            if (dash.StartsWith('r') && dash.Length > 1)
            {
                regex += "-r\\d+";
                continue;
            }
            if (dash.StartsWith("ls") && dash.Length > 2)
            {
                regex += "-ls\\d+";
                continue;
            }
            else
            {
                regex += "-" + dash;
            }
        }

        return regex;
    }
}
