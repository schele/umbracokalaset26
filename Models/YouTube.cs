using System.Text.RegularExpressions;

namespace UmbracoKalaset26.Models;

public static partial class YouTube
{
    /// <summary>
    /// The 11-character video id from a YouTube link (<c>watch?v=</c>, <c>youtu.be/</c>,
    /// <c>/embed/</c>, <c>/shorts/</c>) or a bare id; null when there is none.
    /// </summary>
    public static string? GetVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string trimmed = url.Trim();
        Match match = LinkPattern().Match(trimmed);
        if (match.Success)
        {
            return match.Groups["id"].Value;
        }

        return BareIdPattern().IsMatch(trimmed) ? trimmed : null;
    }

    [GeneratedRegex(@"(?:[?&]v=|youtu\.be/|/embed/|/shorts/)(?<id>[\w-]{11})")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"^[\w-]{11}$")]
    private static partial Regex BareIdPattern();
}
