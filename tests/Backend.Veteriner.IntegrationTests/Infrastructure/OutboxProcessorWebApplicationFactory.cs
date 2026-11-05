using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Paylaşılan VetinityCommandDb_IntegrationTests veritabanında OutboxProcessor çakışmasını önlemek için
/// ayrı LocalDB veritabanı, kayıt tutan sahte SMTP ve kayıt tutan sahte push sağlayıcısı kullanır.
/// </summary>
public sealed class OutboxProcessorWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public OutboxProcessorWebApplicationFactory()
        : base("OutboxProcessorTests")
    {
    }

    protected override bool SeedAuthorization => false;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IEmailSenderImmediate>();
        services.AddSingleton<RecordingEmailSenderImmediate>();
        services.AddSingleton<IEmailSenderImmediate>(sp => sp.GetRequiredService<RecordingEmailSenderImmediate>());

        services.RemoveAll<IPushNotificationProvider>();
        services.AddSingleton<RecordingPushNotificationProvider>();
        services.AddSingleton<IPushNotificationProvider>(sp => sp.GetRequiredService<RecordingPushNotificationProvider>());

        // Barındırılan döngü kapalı: testler partiyi kendi OutboxProcessor örnekleriyle deterministik işletir.
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
    }
}
