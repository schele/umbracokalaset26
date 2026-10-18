using Umbraco.AI.Anthropic;
using Umbraco.AI.Core.Connections;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;
using Umbraco.AI.Core.Settings;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace UmbracoKalaset26.Models;

/// <summary>
/// Development only: when the site has no Umbraco.AI connection yet, creates an Anthropic connection, a chat
/// profile and makes it the default chat profile (used by the AI Summary block). The API key is not stored:
/// the connection references the configuration key <see cref="ApiKeyReference"/> (here the user environment
/// variable Umbraco__AI__Secrets__AnthropicApiKey). Once any connection exists this does nothing, so changes
/// made in the backoffice (AI → Connections / Profiles / Settings) are never touched.
/// </summary>
public sealed class DevelopmentAISetup : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public const string ApiKeyReference = "$Umbraco:AI:Secrets:AnthropicApiKey";
    private const string ChatModel = "claude-sonnet-5-5";

    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DevelopmentAISetup> _logger;

    public DevelopmentAISetup(IHostEnvironment environment, IConfiguration configuration, IServiceScopeFactory scopeFactory, ILogger<DevelopmentAISetup> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return;
        // Whether the key is configured; its value is only ever read by Umbraco.AI through the reference.
        if (string.IsNullOrEmpty(_configuration[ApiKeyReference[1..]]))
        {
            _logger.LogInformation("AI setup: {Key} is not configured, so no Anthropic connection was created.", ApiKeyReference[1..]);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var connections = scope.ServiceProvider.GetRequiredService<IAIConnectionService>();
        var profiles = scope.ServiceProvider.GetRequiredService<IAIProfileService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<IAISettingsService>();

        try
        {
            if ((await connections.GetConnectionsAsync(null!, cancellationToken)).Any()) return;

            var connection = await connections.SaveConnectionAsync(new AIConnection
            {
                Alias = "anthropic",
                Name = "Anthropic",
                ProviderId = "anthropic",
                Settings = new AnthropicProviderSettings { ApiKey = ApiKeyReference },
                IsActive = true,
            }, cancellationToken);

            var profile = await profiles.SaveProfileAsync(new AIProfile
            {
                Alias = "default-chat",
                Name = "Default chat (Claude Sonnet 5.5)",
                Capability = AICapability.Chat,
                Model = new AIModelRef("anthropic", ChatModel),
                ConnectionId = connection.Id,
                Settings = new AIChatProfileSettings(),
                Tags = [],
            }, cancellationToken);

            var settings = await settingsService.GetSettingsAsync(cancellationToken);
            settings.DefaultChatProfileId ??= profile.Id;
            await settingsService.SaveSettingsAsync(settings, cancellationToken);

            _logger.LogInformation("AI setup: created the Anthropic connection and the default chat profile {Profile} ({Model}).", profile.Alias, ChatModel);
        }
        catch (Exception ex)
        {
            // Umbraco.AI wraps the real cause (e.g. "Configuration key … not found") in reflection exceptions.
            var causes = new List<string>();
            for (var e = ex; e is not null; e = e.InnerException) causes.Add($"{e.GetType().Name}: {e.Message}");
            _logger.LogWarning(ex, "AI setup failed: {Causes}", string.Join(" → ", causes));
        }
    }
}

public sealed class DevelopmentAISetupComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, DevelopmentAISetup>();
}
