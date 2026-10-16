using System.Globalization;
using Esatto.Umbraco.Backoffice.AICosts;

namespace UmbracoKalaset26.Models.Charts;

public enum GraphMeasure { Cost, Tokens, Requests }

public enum GraphSplit { Total, Model, Profile, Provider }

/// <summary>A Graph block's settings, parsed from its radio button lists.</summary>
/// <param name="Days">7, 30, 90, 180 or 365 ("12 months").</param>
/// <param name="Currency">Null means the configured default currency (Esatto:AICosts:DefaultCurrency).</param>
public sealed record GraphSettings(ChartKind Kind, GraphMeasure Measure, GraphSplit Split, int Days, string? Currency)
{
    /// <summary>Missing or unknown values fall back to the defaults: Column, Cost, Total, 30 days.</summary>
    public static GraphSettings Parse(string? chart, string? measure, string? split, string? period, string? currency) => new(
        ParseEnum(chart, ChartKind.Column),
        ParseEnum(measure, GraphMeasure.Cost),
        ParseEnum(split, GraphSplit.Total),
        period switch { "7 days" => 7, "90 days" => 90, "180 days" => 180, "12 months" => 365, _ => 30 },
        string.IsNullOrWhiteSpace(currency) || currency.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase)
            ? null
            : currency.Trim().ToUpperInvariant());

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;
}

/// <summary>
/// Turns the AI cost report (Esatto.Umbraco.Backoffice.AICosts) into a chart for the Graph block. Time
/// charts use a column/point per day up to 31 days, per week up to 180 and per month beyond. Only cost
/// has a per-model time series, and profile/provider only have period totals, so those splits fall back
/// to the total over time (with a note) and are shown as they are in a donut.
/// </summary>
public static class AICostChart
{
    /// <summary>Categorical colour slots; more series fold into "Other".</summary>
    public const int MaxSeries = 7;

    /// <summary>A donut stays readable up to about six slices.</summary>
    public const int MaxSlices = 6;

    public static ChartSpec Build(GraphSettings settings, AICostReport report, AICostBreakdown? breakdown, CultureInfo culture)
    {
        var notes = new List<string>();
        var (currency, rate) = Currency(settings, report, notes);
        double Money(decimal amount) => (double)(amount * rate);
        var (format, axisFormat) = Formats(settings.Measure, currency, culture);
        var unpriced = settings.Measure == GraphMeasure.Cost && report.Totals.UnpricedTokens > 0;

        List<ChartSeries> series;
        IReadOnlyList<string> labels = [];
        var split = settings.Split;

        if (settings.Kind == ChartKind.Donut)
        {
            if (split == GraphSplit.Total)
            {
                notes.Add("A donut needs a split, so this one is split by model.");
                split = GraphSplit.Model;
            }

            IEnumerable<(string Name, double? Value)> slices = split == GraphSplit.Model
                ? report.Models.Select(m => (m.ModelId, settings.Measure switch
                {
                    GraphMeasure.Cost => m.Cost is { } cost ? Money(cost) : (double?)null,
                    GraphMeasure.Tokens => m.TotalTokens,
                    _ => (double?)m.Requests,
                }))
                : (breakdown?.Rows ?? []).Select(r => (r.Name, settings.Measure switch
                {
                    GraphMeasure.Cost => r.Cost is { } cost ? Money(cost) : (double?)null,
                    GraphMeasure.Tokens => r.TotalTokens,
                    _ => (double?)r.Requests,
                }));

            series = Fold(slices
                .Where(s => s.Value > 0)
                .OrderByDescending(s => s.Value)
                .Select(s => new ChartSeries(s.Name, [s.Value!.Value]))
                .ToList(), MaxSlices);
        }
        else
        {
            var buckets = Buckets(report.Days.Select(d => d.Date).ToList(), settings.Days, culture);
            labels = buckets.Select(b => b.Label).ToList();
            IReadOnlyList<double> Bucketed(IReadOnlyList<double> daily) =>
                buckets.Select(b => b.Indices.Sum(i => i < daily.Count ? daily[i] : 0)).ToList();

            if (split == GraphSplit.Model && settings.Measure == GraphMeasure.Cost && report.ModelDays.Count > 0)
            {
                series = Fold(report.ModelDays
                    .Select(m => new ChartSeries(m.ModelId, Bucketed(m.Costs.Select(Money).ToList())))
                    .OrderByDescending(s => s.Values.Sum())
                    .ToList(), MaxSeries);
            }
            else
            {
                if (split == GraphSplit.Model && settings.Measure != GraphMeasure.Cost)
                    notes.Add($"{MeasureName(settings.Measure)} per model over time isn't recorded, so this shows the total.");
                else if (split is GraphSplit.Profile or GraphSplit.Provider)
                    notes.Add($"A split by {split.ToString().ToLowerInvariant()} is only available as a donut, so this shows the total.");

                series = [new ChartSeries(MeasureName(settings.Measure), Bucketed(report.Days.Select(d => settings.Measure switch
                {
                    GraphMeasure.Cost => Money(d.Cost),
                    GraphMeasure.Tokens => d.TotalTokens,
                    _ => (double)d.Requests,
                }).ToList()))];
            }
        }

        if (unpriced) notes.Add("Models without a configured price aren't included.");

        var title = $"{MeasureName(settings.Measure)}{(split == GraphSplit.Total ? "" : $" by {split.ToString().ToLowerInvariant()}")}, {PeriodName(settings.Days)}";
        return new ChartSpec(settings.Kind, title, labels, series, format, axisFormat, notes.Count > 0 ? string.Join(" ", notes) : null);
    }

    public static string MeasureName(GraphMeasure measure) => measure.ToString();

    public static string PeriodName(int days) => days >= 365 ? "last 12 months" : $"last {days} days";

    // The wanted currency and its rate from the base currency; the base currency itself when there's no rate.
    private static (string Currency, decimal Rate) Currency(GraphSettings settings, AICostReport report, List<string> notes)
    {
        var wanted = settings.Currency ?? report.DefaultCurrency;
        if (wanted.Equals(report.BaseCurrency, StringComparison.OrdinalIgnoreCase)) return (report.BaseCurrency, 1m);
        if (report.Rates?.PerBaseUnit.TryGetValue(wanted, out var rate) == true && rate > 0) return (wanted, rate);

        if (settings.Measure == GraphMeasure.Cost)
            notes.Add($"Exchange rates for {wanted} are unavailable, so amounts are in {report.BaseCurrency}.");
        return (report.BaseCurrency, 1m);
    }

    private static (Func<double, string> Format, Func<double, string> Axis) Formats(GraphMeasure measure, string currency, CultureInfo culture) => measure switch
    {
        GraphMeasure.Cost => (v => $"{v.ToString("N2", culture)} {currency}", v => $"{v.ToString("#,0.##", culture)} {currency}"),
        GraphMeasure.Tokens => (v => $"{v.ToString("N0", culture)} tokens", v => Compact(v, culture)),
        _ => (v => $"{v.ToString("N0", culture)} requests", v => v.ToString("#,0.##", culture)),
    };

    private static string Compact(double value, CultureInfo culture) => value switch
    {
        >= 1_000_000 => $"{(value / 1_000_000).ToString("0.#", culture)} M",
        >= 1_000 => $"{(value / 1_000).ToString("0.#", culture)} k",
        _ => value.ToString("#,0.##", culture),
    };

    // Days → columns: one per day up to 31 days, per 7 days (labelled by the first) up to 180, per calendar month beyond.
    private static List<(string Label, int[] Indices)> Buckets(IReadOnlyList<DateOnly> days, int period, CultureInfo culture)
    {
        if (period <= 31)
            return days.Select((d, i) => (d.ToString("d MMM", culture), new[] { i })).ToList();

        if (period <= 180)
            return days.Select((d, i) => (d, i))
                .GroupBy(x => x.i / 7)
                .Select(g => (g.First().d.ToString("d MMM", culture), g.Select(x => x.i).ToArray()))
                .ToList();

        return days.Select((d, i) => (d, i))
            .GroupBy(x => (x.d.Year, x.d.Month))
            .Select(g => (new DateOnly(g.Key.Year, g.Key.Month, 1).ToString("MMM yy", culture), g.Select(x => x.i).ToArray()))
            .ToList();
    }

    // Keeps the first max - 1 series and sums the rest into a neutral "Other" (series are sorted largest first).
    private static List<ChartSeries> Fold(List<ChartSeries> series, int max)
    {
        if (series.Count <= max) return series;
        var rest = series.Skip(max - 1).ToList();
        var other = Enumerable.Range(0, rest[0].Values.Count).Select(i => rest.Sum(s => s.Values[i])).ToList();
        return [.. series.Take(max - 1), new ChartSeries("Other", other)];
    }
}
