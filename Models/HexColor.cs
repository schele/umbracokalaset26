using System.Globalization;
using System.Text.RegularExpressions;

namespace UmbracoKalaset26.Models;

public static partial class HexColor
{
    /// <summary>
    /// A color picker value as a CSS <c>#rrggbb</c> color: accepts 3 or 6 hex digits with or
    /// without the leading <c>#</c> (older values were stored without it); null for anything else,
    /// so the value is safe to put in a style attribute.
    /// </summary>
    public static string? Normalize(string? value)
    {
        Match match = HexPattern().Match(value?.Trim() ?? "");
        if (!match.Success)
        {
            return null;
        }

        string digits = match.Groups["digits"].Value;
        if (digits.Length == 3)
        {
            digits = string.Concat(digits.Select(c => $"{c}{c}"));
        }

        return "#" + digits.ToLowerInvariant();
    }

    /// <summary>
    /// Whether light text reads better than dark text on this <c>#rrggbb</c> color
    /// (WCAG relative luminance below the midpoint).
    /// </summary>
    public static bool IsDark(string normalizedHex) => Luminance(normalizedHex) < 0.179;

    /// <summary>
    /// Whether white text meets WCAG AA (4.5:1) on this <c>#rrggbb</c> colour. For brand colours such as
    /// Bootstrap's blue, which sit just above <see cref="IsDark"/>'s midpoint but are designed for white text.
    /// </summary>
    public static bool PrefersLightText(string normalizedHex) => 1.05 / (Luminance(normalizedHex) + 0.05) >= 4.5;

    private static double Luminance(string normalizedHex)
    {
        double Channel(int start)
        {
            double c = int.Parse(normalizedHex.AsSpan(start, 2), NumberStyles.HexNumber) / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    [GeneratedRegex("^#?(?<digits>[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexPattern();
}
