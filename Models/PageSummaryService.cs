using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Umbraco.AI.Core.Chat;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Strings;

namespace UmbracoKalaset26.Models;

/// <summary>
/// The AI Summary block's text: a 2–3 sentence summary of the page, written by Umbraco.AI (default chat
/// profile; usage shows up under AI → Analytics, and in the AI costs) and cached per page, culture and last
/// update, so it is only written again after the page changes (or the site restarts).
/// </summary>
public sealed class PageSummaryService
{
    private const int MaxInput = 8000;
    private static readonly TimeSpan CacheFor = TimeSpan.FromDays(7), RetryFailureAfter = TimeSpan.FromMinutes(10);

    // Only text the editors wrote: never secrets (the Encrypted Textbox), pickers or JSON.
    private static readonly HashSet<string> TextEditors = ["Umbraco.TextBox", "Umbraco.TextArea", "Umbraco.RichText", "Umbraco.BlockList"];

    // Blocks whose content isn't prose for readers (or would summarise the summary).
    private static readonly HashSet<string> SkippedBlocks = ["scriptBlock", "codeBlock", "graphBlock", "formBlock", "aiSummaryBlock", "tableBlock"];

    private readonly IAIChatService _chat;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PageSummaryService> _logger;

    public PageSummaryService(IAIChatService chat, IMemoryCache cache, ILogger<PageSummaryService> logger)
    {
        _chat = chat;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>The summary, or null when the page has no text or the AI call failed (logged).</summary>
    public async Task<string?> GetSummaryAsync(IPublishedContent page, CultureInfo culture)
    {
        var key = $"UmbracoKalaset26.Summary:{page.Key}:{culture.Name}:{page.UpdateDate.Ticks}";
        // One generation per key even when several requests arrive at once (Lazy), shared by all of them.
        var lazy = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return new Lazy<Task<string?>>(() => GenerateAsync(page, culture));
        })!;

        var summary = await lazy.Value;
        if (summary is null) _cache.Set(key, lazy, RetryFailureAfter);
        return summary;
    }

    private async Task<string?> GenerateAsync(IPublishedContent page, CultureInfo culture)
    {
        var text = PageText.Collect([page.Name, .. Texts(page, culture.Name, depth: 0)], MaxInput);
        if (text.Length < 40) return null;

        try
        {
            // Not the request's token: the result is shared with other requests for the same page.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await _chat.GetChatResponseAsync(
                chat => chat.WithAlias("umbracokalaset26-page-summary").WithName("Page summary (AI Summary block)"),
                [
                    new ChatMessage(ChatRole.System,
                        $"You summarise web pages for their visitors. Reply with 2–3 sentences of plain text in {culture.EnglishName}: " +
                        "what the page is about and what the reader will find on it. No heading, no list, no preamble."),
                    new ChatMessage(ChatRole.User, text),
                ],
                timeout.Token);
            var summary = response.Text?.Trim();
            return string.IsNullOrEmpty(summary) ? null : summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI Summary block: could not summarise {Page}.", page.Name);
            return null;
        }
    }

    // The text values of an element's text properties, and of the blocks in its block lists (not too deep).
    private static IEnumerable<string?> Texts(IPublishedElement element, string culture, int depth)
    {
        foreach (var property in element.Properties.Where(p => TextEditors.Contains(p.PropertyType.EditorAlias)))
        {
            var value = property.GetValue(property.PropertyType.VariesByCulture() ? culture : null);
            switch (value)
            {
                case string s:
                    yield return s;
                    break;
                case IHtmlEncodedString html:
                    yield return html.ToHtmlString();
                    break;
                case BlockListModel blocks when depth < 3:
                    foreach (var block in blocks.Where(b => !SkippedBlocks.Contains(b.Content.ContentType.Alias)))
                        foreach (var text in Texts(block.Content, culture, depth + 1))
                            yield return text;
                    break;
            }
        }
    }
}

public sealed class PageSummaryComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<PageSummaryService>();
    }
}
