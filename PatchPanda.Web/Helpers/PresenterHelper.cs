using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace PatchPanda.Web.Helpers;

public static class PresenterHelper
{
    public static MarkupString ToMarkupString(this string input)
    {
        var currentInput = input;

        foreach (var url in Regex.Matches(currentInput, @" (https:\/\/[\S]+)").ToList())
        {
            currentInput = currentInput.Replace(
                url.Value,
                $"<a href=\"{url.Groups[1].Value}\" target=\"_blank\">{url.Groups[1].Value}</a>"
            );
        }

        foreach (
            var url in Regex.Matches(currentInput, @"\[([\S ]+?)\]\((https:\/\/[\S]+?)\)").ToList()
        )
        {
            currentInput = currentInput.Replace(
                url.Value,
                $"<a href=\"{url.Groups[2].Value}\" target=\"_blank\">{url.Groups[1].Value}</a>"
            );
        }

        foreach (var bold in Regex.Matches(currentInput, @"\*\*([\w ]+?)\*\*").ToList())
        {
            currentInput = currentInput.Replace(
                bold.Value,
                $"<strong>{bold.Groups[1].Value}</strong>"
            );
        }

        foreach (var underline in Regex.Matches(currentInput, @"__(.+?)__").ToList())
        {
            currentInput = currentInput.Replace(
                underline.Value,
                $"<u>{underline.Groups[1].Value}</u>"
            );
        }

        foreach (var heading in Regex.Matches(currentInput, @"\n(#+)\s+?([^\n]+)").ToList())
        {
            int headingCount = heading.Groups[1].Value.Length;

            if (headingCount > 6)
                continue;

            currentInput = currentInput.Replace(
                heading.Value,
                $"<h{headingCount}>{heading.Groups[2].Value}</h{headingCount}>"
            );
        }

        currentInput = currentInput.Replace("\r\n", "<br/>").Replace("\n", "<br/>");

        return new MarkupString(currentInput);
    }
}
