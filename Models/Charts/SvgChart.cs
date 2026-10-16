using System.Globalization;
using System.Net;
using System.Text;

namespace UmbracoKalaset26.Models.Charts;

public enum ChartKind { Column, Line, Area, Donut }

/// <summary>One series: a value per label (time charts) or a single value (a donut slice).</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double> Values);

/// <param name="Labels">x-axis labels (time charts); unused by the donut.</param>
/// <param name="Format">A value as shown in tooltips and the table.</param>
/// <param name="AxisFormat">A y-axis tick.</param>
/// <param name="Note">Shown under the chart, e.g. why the requested split isn't available.</param>
public sealed record ChartSpec(
    ChartKind Kind,
    string Title,
    IReadOnlyList<string> Labels,
    IReadOnlyList<ChartSeries> Series,
    Func<double, string> Format,
    Func<double, string> AxisFormat,
    string? Note = null);

/// <summary>
/// Draws a chart as inline SVG on the server (no script, so the page and the backoffice preview look
/// the same): stacked columns, lines, stacked areas or a donut, with a legend for two or more series,
/// a native tooltip on every mark and the values in a "Show data" table. Colours come from
/// wwwroot/css/graph.css (fixed categorical order, "Other" neutral).
/// </summary>
public static class SvgChart
{
    private const double Width = 800, Height = 320, Left = 64, Right = 16, Top = 12, Bottom = 28, Gap = 2;
    private static double PlotW => Width - Left - Right;
    private static double PlotH => Height - Top - Bottom;

    public static string Render(ChartSpec spec)
    {
        var html = new StringBuilder();
        if (spec.Series.Count >= 2) html.Append(Legend(spec.Series));
        html.Append(spec.Kind switch
        {
            ChartKind.Donut => Donut(spec),
            ChartKind.Line => Lines(spec, area: false),
            ChartKind.Area => Lines(spec, area: true),
            _ => Columns(spec),
        });
        if (spec.Note is not null) html.Append($"<p class=\"graph-block__note\">{E(spec.Note)}</p>");
        html.Append(Table(spec));
        return html.ToString();
    }

    /// <summary>The CSS class of series <paramref name="index"/>: categorical slots in order, "Other" neutral.</summary>
    public static string SeriesClass(ChartSeries series, int index) => series.Name == "Other" ? "graph-other" : $"graph-{index + 1}";

    /// <summary>0 plus 3–5 round ticks covering <paramref name="max"/>.</summary>
    public static IReadOnlyList<double> NiceTicks(double max)
    {
        if (max <= 0) return [0, 1];
        var rough = max / 5;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        // 1, 2, 5 only: a 2.5 step puts ticks at 7.5 that whole-number axis formats round to "8".
        var step = new[] { 1, 2, 5, 10 }.Select(m => m * pow).First(s => s >= rough);
        var ticks = new List<double>();
        for (var t = 0d; t <= max + step * 0.999; t += step) ticks.Add(Math.Round(t, 10));
        return ticks;
    }

    private static string Columns(ChartSpec spec)
    {
        var n = spec.Labels.Count;
        var totals = Enumerable.Range(0, n).Select(i => spec.Series.Sum(s => s.Values[i])).ToArray();
        var (ticks, scale) = Scale(totals.DefaultIfEmpty().Max());
        var slot = PlotW / Math.Max(1, n);
        var barW = Math.Max(1, Math.Min(24, slot - Gap));
        var svg = Open(spec, ticks, scale, n);
        for (var i = 0; i < n; i++)
        {
            var x = Left + i * slot + (slot - barW) / 2;
            var y0 = 0d;
            var top = spec.Series.Select(s => s.Values[i] > 0).ToList().LastIndexOf(true);
            for (var s = 0; s < spec.Series.Count; s++)
            {
                var v = spec.Series[s].Values[i];
                if (v <= 0) continue;
                var h = scale(y0 + v) - scale(y0);
                var tip = $"{spec.Labels[i]} · {spec.Series[s].Name}: {spec.Format(v)}";
                var cls = SeriesClass(spec.Series[s], s);
                var baseY = Top + PlotH - scale(y0);
                // The top segment gets a 4px rounded end; segments below it leave a 2px surface gap.
                svg.Append(s == top
                    ? $"<path class=\"{cls}\" d=\"{RoundedTop(x, baseY, barW, h)}\"><title>{E(tip)}</title></path>"
                    : $"<rect class=\"{cls}\" x=\"{F(x)}\" y=\"{F(baseY - h + Gap)}\" width=\"{F(barW)}\" height=\"{F(Math.Max(0, h - Gap))}\"><title>{E(tip)}</title></rect>");
                y0 += v;
            }
        }
        return Close(svg);
    }

    private static string Lines(ChartSpec spec, bool area)
    {
        var n = spec.Labels.Count;
        var max = area
            ? Enumerable.Range(0, n).Select(i => spec.Series.Sum(s => s.Values[i])).DefaultIfEmpty().Max()
            : spec.Series.SelectMany(s => s.Values).DefaultIfEmpty().Max();
        var (ticks, scale) = Scale(max);
        double X(int i) => Left + (n <= 1 ? PlotW / 2 : i * PlotW / (n - 1));
        var svg = Open(spec, ticks, scale, n, centred: false);
        var below = new double[n];
        var marks = new StringBuilder();
        for (var s = 0; s < spec.Series.Count; s++)
        {
            var series = spec.Series[s];
            var cls = SeriesClass(series, s);
            var tops = Enumerable.Range(0, n).Select(i => (area ? below[i] : 0) + series.Values[i]).ToArray();
            var line = string.Join(" ", Enumerable.Range(0, n).Select(i => $"{(i == 0 ? "M" : "L")}{F(X(i))},{F(Top + PlotH - scale(tops[i]))}"));
            if (area)
            {
                var back = string.Join(" ", Enumerable.Range(0, n).Reverse().Select(i => $"L{F(X(i))},{F(Top + PlotH - scale(below[i]))}"));
                svg.Append($"<path class=\"{cls} graph-area\" d=\"{line} {back} Z\"></path>");
            }
            svg.Append($"<path class=\"{cls}-stroke graph-line\" d=\"{line}\"></path>");
            for (var i = 0; i < n; i++)
            {
                // Invisible hover targets bigger than the line, one per point.
                marks.Append($"<circle class=\"graph-hit\" cx=\"{F(X(i))}\" cy=\"{F(Top + PlotH - scale(tops[i]))}\" r=\"7\"><title>{E($"{spec.Labels[i]} · {series.Name}: {spec.Format(series.Values[i])}")}</title></circle>");
            }
            if (area) for (var i = 0; i < n; i++) below[i] = tops[i];
        }
        svg.Append(marks);
        return Close(svg);
    }

    private static string Donut(ChartSpec spec)
    {
        const double cx = Width / 2, cy = Height / 2, outer = 130, inner = 82;
        var values = spec.Series.Select(s => Math.Max(0, s.Values.FirstOrDefault())).ToArray();
        var total = values.Sum();
        var svg = new StringBuilder($"<svg class=\"graph-svg\" viewBox=\"0 0 {F(Width)} {F(Height)}\" role=\"img\" aria-label=\"{E(spec.Title)}. Show data for the values.\">");
        if (total <= 0) return Close(svg);
        var angle = -Math.PI / 2;
        for (var s = 0; s < values.Length; s++)
        {
            if (values[s] <= 0) continue;
            var sweep = values[s] / total * 2 * Math.PI;
            var tip = $"{spec.Series[s].Name}: {spec.Format(values[s])} ({values[s] / total:P0})";
            svg.Append($"<path class=\"{SeriesClass(spec.Series[s], s)} graph-slice\" d=\"{Arc(cx, cy, outer, inner, angle, angle + sweep)}\"><title>{E(tip)}</title></path>");
            angle += sweep;
        }
        svg.Append($"<text class=\"graph-total\" x=\"{F(cx)}\" y=\"{F(cy - 4)}\" text-anchor=\"middle\">{E(spec.Format(total))}</text>");
        svg.Append($"<text class=\"graph-axis\" x=\"{F(cx)}\" y=\"{F(cy + 18)}\" text-anchor=\"middle\">total</text>");
        return Close(svg);
    }

    private static (IReadOnlyList<double> Ticks, Func<double, double> Scale) Scale(double max)
    {
        var ticks = NiceTicks(max);
        var top = ticks[^1] <= 0 ? 1 : ticks[^1];
        return (ticks, v => v / top * PlotH);
    }

    private static StringBuilder Open(ChartSpec spec, IReadOnlyList<double> ticks, Func<double, double> scale, int n, bool centred = true)
    {
        var svg = new StringBuilder($"<svg class=\"graph-svg\" viewBox=\"0 0 {F(Width)} {F(Height)}\" role=\"img\" aria-label=\"{E(spec.Title)}. Show data for the values.\">");
        foreach (var t in ticks)
        {
            var y = Top + PlotH - scale(t);
            svg.Append($"<line class=\"graph-grid\" x1=\"{F(Left)}\" x2=\"{F(Width - Right)}\" y1=\"{F(y)}\" y2=\"{F(y)}\"></line>");
            svg.Append($"<text class=\"graph-axis\" x=\"{F(Left - 8)}\" y=\"{F(y + 4)}\" text-anchor=\"end\">{E(spec.AxisFormat(t))}</text>");
        }
        // About one x label per 90 px.
        var every = Math.Max(1, (int)Math.Ceiling(n / Math.Max(2, Math.Floor(PlotW / 90))));
        var slot = PlotW / Math.Max(1, n);
        for (var i = 0; i < n; i += every)
        {
            var x = centred ? Left + i * slot + slot / 2 : Left + (n <= 1 ? PlotW / 2 : i * PlotW / (n - 1));
            svg.Append($"<text class=\"graph-axis\" x=\"{F(x)}\" y=\"{F(Height - 8)}\" text-anchor=\"middle\">{E(spec.Labels[i])}</text>");
        }
        svg.Append($"<line class=\"graph-baseline\" x1=\"{F(Left)}\" x2=\"{F(Width - Right)}\" y1=\"{F(Top + PlotH)}\" y2=\"{F(Top + PlotH)}\"></line>");
        return svg;
    }

    private static string Close(StringBuilder svg) => svg.Append("</svg>").ToString();

    private static string Legend(IReadOnlyList<ChartSeries> series) =>
        "<ul class=\"graph-legend\">" +
        string.Concat(series.Select((s, i) => $"<li><span class=\"graph-swatch {SeriesClass(s, i)}\"></span>{E(s.Name)}</li>")) +
        "</ul>";

    private static string Table(ChartSpec spec)
    {
        var html = new StringBuilder("<details class=\"graph-data\"><summary>Show data</summary><div class=\"table-responsive\"><table class=\"table table-sm\"><thead><tr>");
        if (spec.Kind == ChartKind.Donut)
        {
            html.Append("<th></th><th class=\"text-end\">Value</th></tr></thead><tbody>");
            foreach (var s in spec.Series) html.Append($"<tr><td>{E(s.Name)}</td><td class=\"text-end\">{E(spec.Format(s.Values.FirstOrDefault()))}</td></tr>");
        }
        else
        {
            html.Append("<th></th>");
            foreach (var s in spec.Series) html.Append($"<th class=\"text-end\">{E(s.Name)}</th>");
            html.Append("</tr></thead><tbody>");
            for (var i = 0; i < spec.Labels.Count; i++)
            {
                html.Append($"<tr><td>{E(spec.Labels[i])}</td>");
                foreach (var s in spec.Series) html.Append($"<td class=\"text-end\">{E(spec.Format(s.Values[i]))}</td>");
                html.Append("</tr>");
            }
        }
        return html.Append("</tbody></table></div></details>").ToString();
    }

    private static string RoundedTop(double x, double baseline, double w, double h)
    {
        var r = Math.Min(4, Math.Min(w / 2, h));
        var top = baseline - h;
        return $"M{F(x)},{F(baseline)} V{F(top + r)} Q{F(x)},{F(top)} {F(x + r)},{F(top)} H{F(x + w - r)} Q{F(x + w)},{F(top)} {F(x + w)},{F(top + r)} V{F(baseline)} Z";
    }

    private static string Arc(double cx, double cy, double outer, double inner, double a0, double a1)
    {
        // A full circle can't be one arc: stop just short of it.
        if (a1 - a0 >= 2 * Math.PI) a1 = a0 + 2 * Math.PI - 0.0001;
        var large = a1 - a0 > Math.PI ? 1 : 0;
        string P(double r, double a) => $"{F(cx + r * Math.Cos(a))},{F(cy + r * Math.Sin(a))}";
        return $"M{P(outer, a0)} A{F(outer)},{F(outer)} 0 {large} 1 {P(outer, a1)} L{P(inner, a1)} A{F(inner)},{F(inner)} 0 {large} 0 {P(inner, a0)} Z";
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
