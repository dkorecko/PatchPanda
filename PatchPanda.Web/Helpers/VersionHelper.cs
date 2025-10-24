using System.Text.RegularExpressions;

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

        regex += "$";

        return regex;
    }

    public static bool IsNewerThan(this string version1, string version2)
    {
        string cleanedVersion1 = version1.TrimStart('v');
        string cleanedVersion2 = version2.TrimStart('v');

        var numbers1 = Regex.Matches(cleanedVersion1, @"\d+");
        var numbers2 = Regex.Matches(cleanedVersion2, @"\d+");

        if (numbers1.Count != numbers2.Count)
            return false;

        for (int i = 0; i < numbers1.Count; i++)
        {
            int num1 = int.Parse(numbers1[i].Value);
            int num2 = int.Parse(numbers2[i].Value);

            if (num1 > num2)
                return true;
            else if (num1 < num2)
                return false;
        }

        return false;
    }
}
