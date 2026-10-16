using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace UmbracoKalaset26.Models;

/// <summary>
/// The Table block's data: what an editor pastes from Excel/Google Sheets (tab-separated) or types as CSV
/// (comma- or semicolon-separated, with "quoted" cells that may contain the separator, "" or line breaks).
/// </summary>
public sealed partial class TableData
{
    private TableData(IReadOnlyList<IReadOnlyList<string>> rows, int columnCount)
    {
        Rows = rows;
        ColumnCount = columnCount;
    }

    /// <summary>Every row has <see cref="ColumnCount"/> cells (short rows are padded with empty cells).</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    public int ColumnCount { get; }

    public static TableData Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new TableData([], 0);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // The separator is whichever the first line uses: tabs (pasted cells), else semicolons (Excel's CSV
        // in Swedish, where the comma is the decimal separator), else commas.
        var firstLine = text.Split('\n')[0];
        var separator = firstLine.Contains('\t') ? '\t'
            : firstLine.Count(c => c == ';') > 0 && firstLine.Count(c => c == ';') >= firstLine.Count(c => c == ',') ? ';'
            : ',';

        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == separator) { row.Add(cell.ToString().Trim()); cell.Clear(); }
            else if (c == '\n') { row.Add(cell.ToString().Trim()); cell.Clear(); rows.Add(row); row = []; }
            else cell.Append(c);
        }
        row.Add(cell.ToString().Trim());
        rows.Add(row);

        rows.RemoveAll(r => r.All(string.IsNullOrEmpty));
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        foreach (var r in rows) while (r.Count < columns) r.Add("");
        return new TableData(rows, columns);
    }

    /// <summary>Whether every non-empty cell below the header row of <paramref name="column"/> is a number
    /// ("1 234,50", "-12", "99%"), so the column can be right-aligned.</summary>
    public bool IsNumeric(int column)
    {
        var cells = Rows.Skip(1).Select(r => r[column]).Where(c => c.Length > 0).ToList();
        return cells.Count > 0 && cells.All(c => NumberPattern().IsMatch(c));
    }

    [GeneratedRegex(@"^[+\-−]?\d[\d\s .,]*%?$")]
    private static partial Regex NumberPattern();
}

public static class FileSize
{
    /// <summary>"512 B", "1.5 kB", "5 MB" (1024-based, one decimal at most, in the page's culture).</summary>
    public static string Format(long bytes, CultureInfo culture)
    {
        string[] units = ["B", "kB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size.ToString(unit == 0 ? "0" : "0.#", culture)} {units[unit]}";
    }
}

/// <summary>Google Maps links for an address; no API key needed.</summary>
public static class MapLinks
{
    public static string Embed(string address) => $"https://www.google.com/maps?q={Uri.EscapeDataString(address.Trim())}&output=embed";

    public static string Open(string address) => $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(address.Trim())}";
}

/// <summary>What the contact form posts. <paramref name="Website"/> is a honeypot: hidden from people, filled in by bots.</summary>
public sealed record ContactFormInput(string? Name, string? Email, string? Message, string? Website)
{
    public const int MaxName = 100, MaxEmail = 254, MaxMessage = 5000;

    public bool IsSpam => !string.IsNullOrEmpty(Website);

    /// <summary>The names of the invalid fields ("name", "email", "message"); empty when the input is fine.
    /// Name and email go into the email's headers, so line breaks there are rejected.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var name = Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > MaxName || name.IndexOfAny(['\r', '\n']) >= 0) errors.Add("name");
        if (!IsEmail(Email)) errors.Add("email");
        var message = Message?.Trim() ?? "";
        if (message.Length == 0 || message.Length > MaxMessage) errors.Add("message");
        return errors;
    }

    private static bool IsEmail(string? value)
    {
        var email = value?.Trim() ?? "";
        if (email.Length == 0 || email.Length > MaxEmail || email.IndexOfAny(['\r', '\n', ' ']) >= 0) return false;
        return MailAddress.TryCreate(email, out var address) && address.Address == email && email.Contains('.', StringComparison.Ordinal);
    }
}

/// <summary>Plain text from a page's content, for the AI summary.</summary>
public static partial class PageText
{
    /// <summary>One line per non-empty value: HTML tags (and script/style content) removed, entities decoded,
    /// whitespace collapsed; cut at <paramref name="maxLength"/> characters.</summary>
    public static string Collect(IEnumerable<string?> values, int maxLength)
    {
        var lines = values
            .Select(v => v ?? "")
            .Select(v => ScriptOrStyle().Replace(v, " "))
            .Select(v => Tag().Replace(v, " "))
            .Select(v => WebUtility.HtmlDecode(v) ?? "")
            .Select(v => Whitespace().Replace(v, " ").Trim())
            .Where(v => v.Length > 0);
        var text = string.Join("\n", lines);
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
