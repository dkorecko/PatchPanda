using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace PatchPanda.Web.Helpers;

public static class PresenterHelper
{
    public static MarkupString ToMarkupString(this string input)
    {
        var currentInput = input.Replace("\r\n", "<br/>").Replace("\n", "<br/>");

        foreach (
            var url in Regex
                .Matches(currentInput, @"\[([a-fA-F0-9]+)\]\((https?:\/\/[^\s\)]+)\)")
                .ToList()
        )
        {
            currentInput = currentInput.Replace(
                url.Value,
                $"<a href=\"{url.Groups[2].Value}\" target=\"_blank\">{url.Groups[1].Value}</a>"
            );
        }

        return new MarkupString(currentInput);
    }
}
