using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Deneme kiracısı kısıtları (TRIAL-004, ADR-001 madde 5; TRIAL_LIMITS_API_CONTRACT.md). Tek kaynak: hatırlatma
/// işleyicisi, rapor dışa aktarımı ve içe aktarma okuma bu sabitleri ve hata üreticilerini kullanır; kiracının
/// kısıta tabi olup olmadığını <see cref="ITrialLimitPolicy"/> söyler. Ücretli (Active) kiracıda kısıt yoktur.
/// </summary>
public static class TrialLimits
{
    /// <summary>Kiracı başına, UTC gün içinde gönderilen (kuyruğa alınan) hatırlatma e-postası tavanı.</summary>
    public const int ReminderEmailsPerDay = 20;

    /// <summary>CSV/XLSX rapor dışa aktarımında satır tavanı (ücretlide <c>ReportsSharedLimits.MaxExportRows</c>).</summary>
    public const int ExportMaxRows = 1_000;

    /// <summary>İçe aktarma dosyasında tablo başına veri satırı tavanı (ücretlide <c>ImportFileLimits.MaxRowsPerTable</c>).</summary>
    public const int ImportMaxRowsPerTable = 500;

    /// <summary>Günlük tavan dolduğunda dispatch log'a yazılan atlama nedeni (<c>Status = Skipped</c>).</summary>
    public const string ReminderDailyLimitSkipReason = "TrialDailyLimit";

    public const string ExportLimitExceededCode = "Trial.ExportLimitExceeded";
    public const string ImportLimitExceededCode = "Trial.ImportLimitExceeded";

    public static Error ExportLimitExceeded() => new(
        ExportLimitExceededCode,
        $"Deneme sürümünde dışa aktarma en fazla {ExportMaxRows} satır sınırı; abonelikle kalkar. Filtreyi daraltın.");

    public static Error ImportLimitExceeded(string tableDisplayName) => new(
        ImportLimitExceededCode,
        $"Deneme sürümünde içe aktarma tablo başına en fazla {ImportMaxRowsPerTable} satır sınırı ({tableDisplayName} tablosu aşıyor); abonelikle kalkar.");
}
