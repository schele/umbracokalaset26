using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Extensions;
using Umbraco.Cms.Web.Website.Controllers;
using UmbracoKalaset26.Models;

namespace UmbracoKalaset26.Controllers;

/// <summary>
/// The Contact Form block posts here (/umbraco/surface/contactform/submit). The recipient is looked up from
/// the page's block on the server, never taken from the form, so the endpoint can't be used to email anyone
/// else. Sends with Umbraco's email sender (Umbraco:CMS:Global:Smtp) and redirects back to the page with the
/// outcome (sent|invalid|unavailable) in TempData under "ContactForm:{block}". TempData is read once, so the
/// page right after shows the outcome and a reload shows the form again.
/// </summary>
public sealed class ContactFormController : SurfaceController
{
    private readonly IPublishedContentCache _contentCache;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ContactFormController> _logger;

    public ContactFormController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        IPublishedContentCache contentCache,
        IEmailSender emailSender,
        ILogger<ContactFormController> logger)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _contentCache = contentCache;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid pageKey, string? culture, Guid blockKey, string? returnUrl,
        string? name, string? email, string? message, string? website)
    {
        var back = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        IActionResult Back(string status)
        {
            // Read by Views/Partials/blocklist/Components/formBlock.cshtml (same key).
            TempData[$"ContactForm:{blockKey:N}"] = status;
            return Redirect($"{back}#form-{blockKey:N}");
        }

        var input = new ContactFormInput(name, email, message, website);
        // A bot filled in the hidden field: pretend it worked, send nothing.
        if (input.IsSpam) return Back("sent");
        if (input.Validate().Count > 0) return Back("invalid");

        var page = await _contentCache.GetByIdAsync(pageKey);
        var block = page is null ? null : FindFormBlock(page, string.IsNullOrWhiteSpace(culture) ? null : culture, blockKey);
        var recipient = block?.Value<string>("recipient")?.Trim();
        if (string.IsNullOrEmpty(recipient)) return Back("unavailable");

        if (!_emailSender.CanSendRequiredEmail())
        {
            _logger.LogWarning("Contact form on {Page}: no SMTP settings (Umbraco:CMS:Global:Smtp), so the message was not sent.", page!.Name);
            return Back("unavailable");
        }

        var body = $"""
            Message from the contact form on {page!.Name} ({back}):

            Name: {input.Name!.Trim()}
            Email: {input.Email!.Trim()}

            {input.Message!.Trim()}
            """;
        try
        {
            await _emailSender.SendAsync(
                new EmailMessage(null, [recipient], null, null, [input.Email.Trim()],
                    $"Contact form: {input.Name.Trim()}", body, false, null),
                emailType: "ContactForm");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Contact form on {Page}: sending the email failed.", page.Name);
            return Back("unavailable");
        }

        return Back("sent");
    }

    // The Contact Form block with this key in any of the page's block lists (posted culture for culture-variant
    // properties). By alias: with ModelsBuilder InMemoryAuto the FormBlock model only exists at runtime.
    private static IPublishedElement? FindFormBlock(IPublishedContent page, string? culture, Guid blockKey) =>
        page.Properties
            .Select(p => p.GetValue(p.PropertyType.VariesByCulture() ? culture : null))
            .OfType<BlockListModel>()
            .SelectMany(list => list)
            .FirstOrDefault(item => item.ContentKey == blockKey && item.Content.ContentType.Alias == "formBlock")?.Content;
}
