using Esatto.Umbraco.Backoffice.ScheduledTasks;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Extensions;

namespace UmbracoKalaset26.Models.ScheduledTasks;

// Demo jobs for Settings → Scheduled Tasks (Esatto.Umbraco.Backoffice.ScheduledTasks), one per feature of the
// dashboard. None of them changes content or calls out of the site: they log, wait and read.

/// <summary>Plain recurring job: a few log lines per run, shown in the run's history.</summary>
public sealed class NewsletterDigestJob(ILogger<NewsletterDigestJob> logger) : RecurringBackgroundJobBase(TimeSpan.FromHours(1))
{
    public override TimeSpan Delay => TimeSpan.FromMinutes(1);

    public override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Collecting this hour's new pages for the newsletter digest");
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var subscribers = Random.Shared.Next(40, 120);
        logger.LogInformation("Digest ready for {Subscribers} subscribers (demo: nothing is sent)", subscribers);
    }
}

/// <summary>Progress bar while it runs (about 30 seconds), and stoppable: it passes its token on.</summary>
public sealed class SearchIndexRebuildJob(IScheduledTaskProgress progress, ILogger<SearchIndexRebuildJob> logger)
    : RecurringBackgroundJobBase(TimeSpan.FromHours(6))
{
    private const int Pages = 60;

    public override TimeSpan Delay => TimeSpan.FromMinutes(2);

    public override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Rebuilding the search index for {Pages} pages", Pages);
        for (var i = 1; i <= Pages; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            progress.Report(this, 100.0 * i / Pages, $"Indexed {i} of {Pages} pages");
        }

        logger.LogInformation("Search index rebuilt (demo: no index was touched)");
    }
}

public sealed class OutdatedPagesReportProperties
{
    [ScheduledTaskProperty("Start page", Description = "Only pages **below** this page are checked.", Mandatory = true)]
    public Guid? StartPage { get; set; }

    [ScheduledTaskProperty("Not updated for (days)")]
    public int OlderThanDays { get; set; } = 365;

    [ScheduledTaskProperty("Include unpublished pages")]
    public bool IncludeUnpublished { get; set; }
}

/// <summary>
/// Properties set in the dashboard (a content tree picker, a number, a toggle). Read-only: it lists the pages
/// below the start page that haven't been updated for a while, and changes nothing.
/// </summary>
public sealed class OutdatedPagesReportJob(
    IScheduledTaskProperties properties,
    IContentService contentService,
    ILogger<OutdatedPagesReportJob> logger)
    : RecurringBackgroundJobBase(TimeSpan.FromDays(1)), IScheduledTaskWithProperties<OutdatedPagesReportProperties>
{
    public override Task RunJobAsync(CancellationToken cancellationToken)
    {
        var settings = properties.Get(this);
        if (settings.StartPage is not { } startKey || contentService.GetById(startKey) is not { } start)
        {
            logger.LogWarning("No start page: pick one under Properties in Settings → Scheduled Tasks");
            return Task.CompletedTask;
        }

        var cutoff = DateTime.Now.AddDays(-settings.OlderThanDays);
        var outdated = new List<IContent>();
        long page = 0, total;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = contentService.GetPagedDescendants(start.Id, page++, 100, out total);
            outdated.AddRange(batch.Where(c => c.UpdateDate < cutoff && (settings.IncludeUnpublished || c.Published)));
        }
        while (page * 100 < total);

        foreach (var content in outdated.Take(20))
        {
            logger.LogInformation("{Page} was last updated {Updated:d}", content.Name, content.UpdateDate);
        }

        logger.LogInformation("{Count} of {Total} pages below {Start} not updated for {Days} days",
            outdated.Count, total, start.Name, settings.OlderThanDays);
        return Task.CompletedTask;
    }
}

/// <summary>Retries and a time limit: fails about half the time, as a flaky feed would.</summary>
[ScheduledTaskRunPolicy(Retries = 2, RetryDelaySeconds = 30, TimeoutSeconds = 60)]
public sealed class ProductFeedImportJob(ILogger<ProductFeedImportJob> logger) : RecurringBackgroundJobBase(TimeSpan.FromHours(1))
{
    public override TimeSpan Delay => TimeSpan.FromMinutes(3);

    public override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Downloading the product feed");
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        if (Random.Shared.Next(2) == 0)
        {
            throw new TimeoutException("The product feed didn't answer within 30 seconds (demo failure)");
        }

        logger.LogInformation("Imported {Products} products (demo: nothing was saved)", Random.Shared.Next(200, 900));
    }
}

/// <summary>Distributed job: runs on one server at a time, coordinated through the database.</summary>
public sealed class SitemapPingJob(ILogger<SitemapPingJob> logger) : IDistributedBackgroundJob
{
    public string Name => "SitemapPing";

    public TimeSpan Period => TimeSpan.FromMinutes(30);

    public Task ExecuteAsync() => ExecuteAsync(CancellationToken.None);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Would ping the search engines about the sitemap (demo: no request is made)");
        return Task.CompletedTask;
    }
}

public sealed class DemoScheduledTasksComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddRecurringBackgroundJob<NewsletterDigestJob>();
        builder.Services.AddRecurringBackgroundJob<SearchIndexRebuildJob>();
        builder.Services.AddRecurringBackgroundJob<OutdatedPagesReportJob>();
        builder.Services.AddRecurringBackgroundJob<ProductFeedImportJob>();
        builder.Services.AddSingleton<IDistributedBackgroundJob, SitemapPingJob>();
    }
}
