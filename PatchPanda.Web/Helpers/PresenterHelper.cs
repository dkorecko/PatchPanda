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

        foreach (var bold in Regex.Matches(currentInput, @"\*\*([\w ]+)\*\*").ToList())
        {
            currentInput = currentInput.Replace(
                bold.Value,
                $"<strong>{bold.Groups[1].Value}</strong>"
            );
        }

        currentInput = currentInput.Replace("\r\n", "<br/>").Replace("\n", "<br/>");

        return new MarkupString(currentInput);
    }
}
