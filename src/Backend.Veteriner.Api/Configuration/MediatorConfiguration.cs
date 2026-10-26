using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Api.Configuration;

/// <summary>
/// Mediator kaydı (ADR-038). Kaynak üretici yalnız bu projede çalışır; seçenekler derleme zamanı sabiti olmalıdır.
/// </summary>
public static class MediatorConfiguration
{
    public static IServiceCollection AddBackendMediator(this IServiceCollection services)
    {
        services.AddMediator(options =>
        {
            // İşleyiciler ve ara katmanlar DbContext gibi kapsamlı bağımlılık taşır (kütüphane varsayılanı Singleton).
            options.ServiceLifetime = ServiceLifetime.Scoped;

            // Domain: IDomainEvent bildirimleri; Application: komut, sorgu ve işleyiciler.
            options.Assemblies = [typeof(IDomainEvent), typeof(Application.DependencyInjection)];

            // Ara katman sırası dıştan içe (ilk eleman en dışta çalışır):
            // 1) UnhandledException: iç katmanlardaki istisnaları loglar ve aynen iletir.
            // 2) Validation: geçersiz istek işleyiciye ulaşmadan kesilir.
            // 3) TenantSubscriptionWriteGuard: salt okunur / iptal kiracıda *Command yazmalarını reddeder.
            // 4) Audit: IAuditableRequest için denetim kaydı (başarılı / başarısız).
            // 5) ActivationSignal: başarılı IActivationSignalRequest sonrası ilk aksiyon işareti (transaction dışında).
            // 6) Performance: yavaş işleyici uyarısı.
            // 7) Logging: istek başı / sonu logu.
            // 8) Transaction (Infrastructure): ITransactionalRequest için DB transaction; işleyiciye en yakın katman.
            options.PipelineBehaviors =
            [
                typeof(UnhandledExceptionBehavior<,>),
                typeof(ValidationBehavior<,>),
                typeof(TenantSubscriptionWriteGuardBehavior<,>),
                typeof(AuditBehavior<,>),
                typeof(ActivationSignalBehavior<,>),
                typeof(PerformanceBehavior<,>),
                typeof(LoggingBehavior<,>),
                typeof(TransactionBehavior<,>),
            ];
        });

        return services;
    }
}
