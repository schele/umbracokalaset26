using System.Globalization;
using Esatto.Umbraco.Backoffice.AICosts;
using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace UmbracoKalaset26.Models.Charts;

/// <summary>
/// Data for the Graph block: the AI cost report (and the profile/provider breakdown for donuts), cached
/// for 15 minutes per period so a public page doesn't run the usage queries on every request.
/// </summary>
public sealed class AICostChartService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;
    private readonly AICostReportService _reports;
    private readonly AICostBreakdownService _breakdowns;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AICostChartService> _logger;

    public AICostChartService(IMemoryCache cache, AICostReportService reports, AICostBreakdownService breakdowns,
        IConfiguration configuration, ILogger<AICostChartService> logger)
    {
        _cache = cache;
        _reports = reports;
        _breakdowns = breakdowns;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>The chart, or null when the usage data can't be read (logged; the block shows a message).</summary>
    public async Task<ChartSpec?> GetChartAsync(GraphSettings settings, CancellationToken cancellationToken)
    {
        if (_configuration.GetValue<bool>("Graph:SampleData"))
        {
            // Made-up usage (appsettings.Development.json), labelled as such under the chart.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var sample = AICostChart.Build(settings, AICostSampleData.Report(settings.Days, today),
                settings.Split is GraphSplit.Profile or GraphSplit.Provider
                    ? AICostSampleData.Breakdown(settings.Split == GraphSplit.Profile ? AICostDimension.Profile : AICostDimension.Provider, settings.Days, today)
                    : null,
                CultureInfo.CurrentCulture);
            return sample with { Note = $"Sample data, not real usage.{(sample.Note is null ? "" : " " + sample.Note)}" };
        }

        try
        {
            var report = await _cache.GetOrCreateAsync($"UmbracoKalaset26.Graph.Report:{settings.Days}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheFor;
                return _reports.GetReportAsync(settings.Days, cancellationToken);
            });

            AICostBreakdown? breakdown = null;
            if (settings.Kind == ChartKind.Donut && settings.Split is GraphSplit.Profile or GraphSplit.Provider)
            {
                var dimension = settings.Split == GraphSplit.Profile ? AICostDimension.Profile : AICostDimension.Provider;
                breakdown = await _cache.GetOrCreateAsync($"UmbracoKalaset26.Graph.Breakdown:{dimension}:{settings.Days}", entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheFor;
                    return _breakdowns.GetBreakdownAsync(dimension, settings.Days, cancellationToken);
                });
            }

            return AICostChart.Build(settings, report!, breakdown, CultureInfo.CurrentCulture);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Graph block: could not load the AI cost data.");
            return null;
        }
    }
}

public sealed class AICostChartComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // AddAICosts is idempotent; the package's own composer calls it too.
        builder.Services.AddAICosts();
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<AICostChartService>();
    }
}
