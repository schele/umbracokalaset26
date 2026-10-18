using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ColorCode;
using ColorCode.Styling;
using Microsoft.AspNetCore.Html;

namespace UmbracoKalaset26.Models;

/// <summary>
/// Visual Studio-style syntax colouring for the code block, done on the server with ColorCode
/// (Microsoft's colouriser), so the page and the backoffice preview need no JavaScript.
/// </summary>
public static partial class CodeHighlighter
{
    // Keys are the Language dropdown's values (uSync/v18/DataTypes/CodeBlockLanguage.config).
    // ColorCode has no JSON mode; JavaScript colours it correctly. C# goes to CSharpHighlighter instead.
    private static readonly Dictionary<string, ILanguage> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C#"] = ColorCode.Languages.CSharp,
        ["JavaScript"] = ColorCode.Languages.JavaScript,
        ["TypeScript"] = ColorCode.Languages.Typescript,
        ["HTML"] = ColorCode.Languages.Html,
        ["CSS"] = ColorCode.Languages.Css,
        ["JSON"] = ColorCode.Languages.JavaScript,
        ["SQL"] = ColorCode.Languages.Sql,
        ["XML"] = ColorCode.Languages.Xml,
        ["PowerShell"] = ColorCode.Languages.PowerShell,
    };

    /// <summary>The coloured languages, in dropdown order ("Plain text" is the uncoloured fallback).</summary>
    public static IReadOnlyCollection<string> Languages => LanguageMap.Keys;

    /// <summary>
    /// The code as HTML: a <c>&lt;pre&gt;</c> with one <c>.code-line</c> per line (CSS numbers them),
    /// coloured for a known language, otherwise plain. Code is always shown as text, never markup.
    /// </summary>
    public static IHtmlContent Highlight(string? code, string? language, bool dark)
    {
        // One newline style, and no empty numbered line after a trailing newline.
        code = code?.Replace("\r\n", "\n").TrimEnd('\n');
        if (string.IsNullOrEmpty(code))
        {
            return HtmlString.Empty;
        }

        // C# is coloured from Roslyn's syntax tree, like Visual Studio: ColorCode only knows keywords.
        if (string.Equals(language, "C#", StringComparison.OrdinalIgnoreCase))
        {
            string theme = dark ? "color:#DADADA;background-color:#1E1E1E;" : "color:#000000;background-color:#FFFFFF;";
            return new HtmlString($"<div style=\"{theme}\"><pre>{NumberLines(CSharpHighlighter.ToHtml(code, dark))}</pre></div>");
        }

        if (language is null || !LanguageMap.TryGetValue(language, out ILanguage? colorCodeLanguage))
        {
            return new HtmlString($"<pre>{NumberLines(WebUtility.HtmlEncode(code))}</pre>");
        }

        // ColorCode returns <div style="(theme colours)"><pre>\n(coloured code)\n</pre></div>.
        // Its own line breaks are Environment.NewLine (\r\n on Windows).
        string html = new HtmlFormatter(dark ? StyleDictionary.DefaultDark : StyleDictionary.DefaultLight)
            .GetHtmlString(code, colorCodeLanguage)
            .Replace("\r\n", "\n");
        Match parts = ColorCodeOutput().Match(html);
        return parts.Success
            ? new HtmlString($"{parts.Groups["open"].Value}<pre>{NumberLines(parts.Groups["code"].Value)}</pre></div>")
            : new HtmlString(html);
    }

    /// <summary>
    /// Wraps each line in <c>&lt;span class="code-line"&gt;</c>. A colour span that crosses a line break
    /// (a multi-line comment or string) is closed at the end of the line and reopened on the next,
    /// so every line is well-formed on its own.
    /// </summary>
    private static string NumberLines(string html)
    {
        var result = new StringBuilder();
        var open = new List<string>();
        foreach (string line in html.Split('\n'))
        {
            result.Append("<span class=\"code-line\">");
            open.ForEach(tag => result.Append(tag));
            result.Append(line);
            foreach (Match tag in SpanTag().Matches(line))
            {
                if (tag.Value.StartsWith("</", StringComparison.Ordinal))
                {
                    open.RemoveAt(open.Count - 1);
                }
                else
                {
                    open.Add(tag.Value);
                }
            }

            result.Insert(result.Length, "</span>", open.Count + 1);
        }

        return result.ToString();
    }

    [GeneratedRegex(@"^(?<open><div[^>]*>)<pre>\n?(?<code>.*?)\n?</pre></div>$", RegexOptions.Singleline)]
    private static partial Regex ColorCodeOutput();

    [GeneratedRegex("<span[^>]*>|</span>")]
    private static partial Regex SpanTag();
}
