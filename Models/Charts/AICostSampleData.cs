using Esatto.Umbraco.Backoffice.AICosts;

namespace UmbracoKalaset26.Models.Charts;

/// <summary>
/// Made-up AI usage for trying the Graph block without real Umbraco.AI usage (turned on by
/// <c>Graph:SampleData</c>, development only). Four models with a weekday rhythm, quieter weekends and
/// growth over the year. A day's values depend only on its date, so every period agrees with the others.
/// </summary>
public static class AICostSampleData
{
    private sealed record SampleModel(string Id, string Provider, double CostPerDay, double UsdPerMillionTokens, int TokensPerRequest, bool Spiky = false);

    private static readonly SampleModel[] Models =
    [
        new("claude-sonnet-5-5", "Anthropic", 2.4, 6, 3000),
        new("claude-haiku-5-5", "Anthropic", 0.7, 1.5, 1500),
        new("claude-opus-5-5", "Anthropic", 0.9, 30, 5000, Spiky: true),
        new("gpt-5-mini", "OpenAI", 0.35, 0.8, 1200),
    ];

    private static readonly (string Name, decimal Share)[] Profiles =
        [("Content writer", 0.45m), ("Translator", 0.25m), ("SEO assistant", 0.18m), ("Alt text", 0.12m)];

    private static readonly ExchangeRates Rates = new(new DateOnly(2026, 10, 15), "Sample rates",
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1m, ["SEK"] = 10.5m, ["EUR"] = 0.92m, ["NOK"] = 10.8m, ["DKK"] = 6.9m, ["GBP"] = 0.79m,
        });

    private sealed record Usage(decimal Cost, long Requests, long Input, long Cached, long Output)
    {
        public long Total => Input + Output;
    }

    public static AICostReport Report(int days, DateOnly today)
    {
        var first = today.AddDays(-(days - 1));
        var dates = Enumerable.Range(0, days).Select(first.AddDays).ToList();
        var usage = Models.Select((m, mi) => dates.Select(d => Day(m, mi, d, today)).ToList()).ToList();

        var dayList = dates.Select((date, i) =>
        {
            var day = usage.Select(u => u[i]).ToList();
            var requests = day.Sum(u => u.Requests);
            var failed = requests / 100;
            return new AICostDay(date, requests, day.Sum(u => u.Input), day.Sum(u => u.Output), day.Sum(u => u.Cached),
                day.Sum(u => u.Total), day.Sum(u => u.Cost), 0, requests - failed, failed);
        }).ToList();

        var models = Models.Select((m, mi) => new AICostModel(m.Id, usage[mi].Sum(u => u.Requests), usage[mi].Sum(u => u.Input),
            usage[mi].Sum(u => u.Output), usage[mi].Sum(u => u.Cached), usage[mi].Sum(u => u.Total), usage[mi].Sum(u => u.Cost))).ToList();

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthDays = Enumerable.Range(0, today.Day).Select(i => monthStart.AddDays(i))
            .Select(d => new AICostMonthDay(d, Models.Select((m, mi) => Day(m, mi, d, today).Cost).Sum())).ToList();
        var monthCost = monthDays.Sum(d => d.Cost);

        return new AICostReport(
            first, today, "USD", "SEK", ["SEK", "EUR", "USD", "NOK", "DKK", "GBP"], Rates, "Sample data",
            dayList,
            models.OrderByDescending(m => m.Cost).ToList(),
            new AICostTotals(dayList.Sum(d => d.Requests), dayList.Sum(d => d.TotalTokens), dayList.Sum(d => d.Cost), 0),
            new AICostMonth(monthStart, today, monthCost, monthCost * DateTime.DaysInMonth(today.Year, today.Month) / today.Day, 0, null, monthDays),
            null,
            Models.Select((m, mi) => new AICostModelDays(m.Id, usage[mi].Select(u => u.Cost).ToList())).ToList(),
            0,
            1800);
    }

    /// <summary>The period split by profile (fixed shares) or provider (by model); rows add up to the report.</summary>
    public static AICostBreakdown Breakdown(AICostDimension dimension, int days, DateOnly today)
    {
        var report = Report(days, today);
        if (dimension == AICostDimension.Provider)
        {
            return new AICostBreakdown("provider", report.Models
                .GroupBy(m => Models.First(s => s.Id == m.ModelId).Provider)
                .Select(g => new AICostBreakdownRow(g.Key, g.Key, g.Sum(m => m.Requests), g.Sum(m => m.InputTokens), g.Sum(m => m.CachedInputTokens),
                    g.Sum(m => m.OutputTokens), g.Sum(m => m.TotalTokens), g.Sum(m => m.Cost ?? 0), 0))
                .OrderByDescending(r => r.Cost)
                .ToList());
        }

        // Profiles (and users, should anyone ask): fixed shares, the last row takes the remainder so rows add up.
        var t = report.Models;
        var rows = new List<AICostBreakdownRow>();
        decimal cost = 0;
        long requests = 0, input = 0, cached = 0, output = 0;
        for (var i = 0; i < Profiles.Length; i++)
        {
            var (name, share) = Profiles[i];
            var last = i == Profiles.Length - 1;
            long Part(long total, long used) => last ? total - used : (long)(total * share);
            var row = new AICostBreakdownRow(name.ToLowerInvariant().Replace(' ', '-'), name,
                Part(t.Sum(m => m.Requests), requests), Part(t.Sum(m => m.InputTokens), input), Part(t.Sum(m => m.CachedInputTokens), cached),
                Part(t.Sum(m => m.OutputTokens), output), 0,
                last ? report.Totals.Cost - cost : Math.Round(report.Totals.Cost * share, 6), 0);
            row = row with { TotalTokens = row.InputTokens + row.OutputTokens };
            cost += row.Cost!.Value;
            requests += row.Requests;
            input += row.InputTokens;
            cached += row.CachedInputTokens;
            output += row.OutputTokens;
            rows.Add(row);
        }
        return new AICostBreakdown(dimension.ToString().ToLowerInvariant(), rows);
    }

    private static Usage Day(SampleModel model, int index, DateOnly date, DateOnly today)
    {
        var age = today.DayNumber - date.DayNumber;
        var growth = 1 / (1 + age / 200.0);
        var weekday = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0.3 : 1;
        var noise = Noise(date.DayNumber, index);
        // The big model is used in bursts: a few heavy days, otherwise little.
        if (model.Spiky) noise = noise > 1.15 ? 3.5 : 0.5;

        var cost = Math.Round((decimal)(model.CostPerDay * growth * weekday * noise), 6);
        var tokens = (long)((double)cost / model.UsdPerMillionTokens * 1_000_000);
        var input = tokens * 4 / 5;
        return new Usage(cost, Math.Max(1, tokens / model.TokensPerRequest), input, input * 3 / 10, tokens - input);
    }

    // Deterministic 0.75–1.25 per date and model.
    private static double Noise(int dayNumber, int model)
    {
        var x = (uint)dayNumber * 2654435761u ^ (uint)(model + 1) * 40503u;
        x ^= x >> 13;
        x *= 0x5bd1e995;
        x ^= x >> 15;
        return 0.75 + x % 1000 / 2000.0;
    }
}
