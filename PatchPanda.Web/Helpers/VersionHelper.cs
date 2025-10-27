using System.Text.RegularExpressions;

namespace PatchPanda.Web.Helpers;

public static class VersionHelper
{
    public static string BuildRegexFromVersion(string version)
    {
        string regex = "^";

        if (version.Contains('@'))
        {
            var atSplit = version.Split('@');
            regex += Regex.Escape(atSplit[0]) + "@";
        }
        else if (version.StartsWith('v'))
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

    public static bool IsSameVersionAs(this string version1, string version2)
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
            if (num1 != num2)
                return false;
        }

        return true;
    }

    public static bool IsNewerThan(this string version1, string version2)
    {
        string cleanedVersion1 = version1.TrimStart('v');
        string cleanedVersion2 = version2.TrimStart('v');

        if (cleanedVersion1.Contains('@'))
            cleanedVersion1 = cleanedVersion1.Split('@')[1];

        if (cleanedVersion2.Contains('@'))
            cleanedVersion2 = cleanedVersion2.Split('@')[1];

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

    public static int NewerComparison(string version1, string version2)
    {
        if (version1.IsNewerThan(version2))
            return -1;
        else if (version2.IsNewerThan(version1))
            return 1;
        else
            return 0;
    }
}
