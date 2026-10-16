using System.Globalization;
using System.Text.Json;

namespace UmbracoKalaset26.Models;

/// <summary>
/// A Date Range property (Esatto CustomEditors), stored as JSON <c>{ "from": "...", "to": "..." }</c> with
/// ISO 8601 values ("2026-05-01", or "2026-05-01T09:00:00" when the data type includes time), each null
/// when unset.
/// </summary>
public sealed record DateRangeValue(DateTime? From, DateTime? To, bool HasTime)
{
    public static readonly DateRangeValue Empty = new(null, null, false);

    public bool IsEmpty => From is null && To is null;

    /// <summary>Reads the property value (a JsonDocument from Umbraco.Plain.Json, or JSON text); anything else is empty.</summary>
    public static DateRangeValue Parse(object? value)
    {
        switch (value)
        {
            case JsonDocument document:
                return Parse(document.RootElement);
            case JsonElement element:
                if (element.ValueKind != JsonValueKind.Object) return Empty;
                var from = Read(element, "from");
                var to = Read(element, "to");
                return new DateRangeValue(from.Date, to.Date, from.HasTime || to.HasTime);
            case null:
                return Empty;
            default:
                var json = value.ToString();
                if (string.IsNullOrWhiteSpace(json)) return Empty;
                try
                {
                    using var parsed = JsonDocument.Parse(json);
                    return Parse(parsed.RootElement);
                }
                catch (JsonException)
                {
                    return Empty;
                }
        }
    }

    /// <summary>
    /// The range as people write it: "12–15 October 2026", "30 October – 2 November 2026",
    /// "12 October 2026 09:00 – 17:30"; "From …" or "Until …" when only one end is set.
    /// </summary>
    public string Format(CultureInfo culture)
    {
        var swedish = culture.TwoLetterISOLanguageName == "sv";
        string Day(DateTime d) => d.ToString("d MMMM yyyy", culture) + (HasTime ? d.ToString(" HH:mm", culture) : "");

        if (From is not { } from)
            return To is { } until ? $"{(swedish ? "Till" : "Until")} {Day(until)}" : "";
        if (To is not { } to)
            return $"{(swedish ? "Från" : "From")} {Day(from)}";

        if (HasTime)
            return from.Date == to.Date
                ? $"{Day(from)} – {to.ToString("HH:mm", culture)}"
                : $"{Day(from)} – {Day(to)}";

        if (from.Date == to.Date) return Day(from);
        if (from.Year == to.Year && from.Month == to.Month)
            return $"{from.Day}–{to.ToString("d MMMM yyyy", culture)}";
        if (from.Year == to.Year)
            return $"{from.ToString("d MMMM", culture)} – {to.ToString("d MMMM yyyy", culture)}";
        return $"{Day(from)} – {Day(to)}";
    }

    private static (DateTime? Date, bool HasTime) Read(JsonElement range, string name) =>
        range.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? (date, value.GetString()!.Contains('T'))
            : (null, false);
}
