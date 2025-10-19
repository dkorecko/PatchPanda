using System.Runtime.InteropServices.Marshalling;
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

        var periodSplit1 = cleanedVersion1.Split('.');
        var periodSplit2 = cleanedVersion2.Split('.');

        for (int i = 0; i < periodSplit1.Length; i++)
        {
            bool result1 = int.TryParse(periodSplit1[i], out int num1);
            bool result2 = int.TryParse(periodSplit2[i], out int num2);

            if (!result1 || !result2)
                return false;

            if (num1 > num2)
                return true;
            else if (num1 < num2)
                return false;
        }

        var dashSplit1 = periodSplit1[^1].Split('-');
        var dashSplit2 = periodSplit2[^1].Split('-');

        for (int i = 0; i < dashSplit1.Length; i++)
        {
            bool result1 = int.TryParse(dashSplit1[i], out int num1);
            bool result2 = int.TryParse(dashSplit2[i], out int num2);

            if (!result1 || !result2)
            {
                // contains something else (like -r or -ls), let's go to regex
                var match1 = Regex.Match(dashSplit1[i], @"\d+");
                var match2 = Regex.Match(dashSplit2[i], @"\d+");

                if (match1.Success && match2.Success)
                {
                    if (
                        !dashSplit1[i].StartsWith(dashSplit2[i].Replace(match2.Value, string.Empty))
                    )
                        return false;

                    num1 = int.Parse(match1.Value);
                    num2 = int.Parse(match2.Value);

                    if (num1 > num2)
                        return true;
                    else if (num1 < num2)
                        return false;
                }
                else
                {
                    return false;
                }
            }

            if (num1 > num2)
                return true;
            else if (num1 < num2)
                return false;
        }

        return false;
    }
}
