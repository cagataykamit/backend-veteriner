using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Ai;

/// <summary>AI uçlarının sabit hataları (docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §5). AI hatası klinik iş akışını durdurmaz (ADR-008 madde 6).</summary>
public static class AiErrors
{
    public static readonly Error NotConfigured =
        new("Ai.NotConfigured", "AI özelliği bu sunucuda yapılandırılmamış.");

    public static readonly Error ClinicNotEnabled =
        new("Ai.ClinicNotEnabled", "AI özellikleri bu klinikte açık değil.");

    /// <summary>AI kullanımı kliniğe yazılır; aktif klinik seçili olmalıdır.</summary>
    public static readonly Error ClinicSelectionRequired =
        new("Clinics.ClinicSelectionRequired", "AI özelliği için önce klinik seçin.");

    public static readonly Error ClinicNotFound =
        new("Ai.ClinicNotFound", "Klinik bulunamadı.");

    public static readonly Error RateLimited =
        new("Ai.RateLimited", "Kısa sürede çok fazla AI isteği yapıldı; bir dakika sonra tekrar deneyin.");

    public static readonly Error ClinicQuotaExceeded =
        new("Ai.ClinicQuotaExceeded", "Kliniğin bu ayki AI kullanım sınırı doldu. Klinik işleri AI olmadan devam eder.");

    public static readonly Error SystemBudgetExhausted =
        new("Ai.SystemBudgetExhausted", "AI özellikleri şu an kullanılamıyor; daha sonra tekrar deneyin.");

    public static readonly Error ProviderUnavailable =
        new("Ai.ProviderUnavailable", "AI hizmetine şu an ulaşılamıyor; daha sonra tekrar deneyin.");

    public static readonly Error ProviderRejected =
        new("Ai.ProviderRejected", "AI hizmeti isteği kabul etmedi; sorun sürerse destekle iletişime geçin.");

    public static readonly Error InvalidOutput =
        new("Ai.InvalidOutput", "AI geçerli bir özet üretemedi; tekrar deneyin.");

    public static readonly Error Refused =
        new("Ai.Refused", "AI bu kayıtlar için özet üretmedi.");
}
