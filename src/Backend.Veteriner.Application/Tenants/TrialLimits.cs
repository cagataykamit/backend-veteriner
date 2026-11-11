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

    /// <summary>CSV/XLSX/PDF rapor dışa aktarımında satır tavanı (ücretlide <c>ReportsSharedLimits.MaxExportRows</c>; PDF'te <c>MaxPdfExportRows</c>).</summary>
    public const int ExportMaxRows = 1_000;

    /// <summary>İçe aktarma dosyasında tablo başına veri satırı tavanı (ücretlide <c>ImportFileLimits.MaxRowsPerTable</c>).</summary>
    public const int ImportMaxRowsPerTable = 500;

    /// <summary>Günlük tavan dolduğunda dispatch log'a yazılan atlama nedeni (<c>Status = Skipped</c>).</summary>
    public const string ReminderDailyLimitSkipReason = "TrialDailyLimit";

    /// <summary>
    /// Kullanım kotası (TRIAL-006): deneme süresince kiracıdaki toplam müşteri. Günlük işi engellemez (14 günde küçük klinik
    /// yüzlerce kayıt açar); içe aktarmanın tablo başına 500 satırı iki partiye sığar.
    /// </summary>
    public const int MaxClients = 1_000;

    /// <summary>Kullanım kotası (TRIAL-006): deneme süresince kiracıdaki toplam hasta (müşteri başına ortalama iki).</summary>
    public const int MaxPets = 2_000;

    /// <summary>Kullanım kotası (TRIAL-006): silinmemiş dosyaların toplam boyutu (dosya başına 15 MB ile en kötü durumda ~33 dosya).</summary>
    public const long MaxStorageBytes = 500L * 1024 * 1024;

    public const string ExportLimitExceededCode = "Trial.ExportLimitExceeded";
    public const string ImportLimitExceededCode = "Trial.ImportLimitExceeded";
    public const string ClientQuotaExceededCode = "Trial.ClientQuotaExceeded";
    public const string PetQuotaExceededCode = "Trial.PetQuotaExceeded";
    public const string StorageQuotaExceededCode = "Trial.StorageQuotaExceeded";

    /// <summary>Deneme kiracısında canlı ortamda gerçek e-belge kesilmez (ADR-036 ilke 7).</summary>
    public const string EDocumentsNotAllowedCode = "Trial.EDocumentsNotAllowed";

    public static Error ExportLimitExceeded() => new(
        ExportLimitExceededCode,
        $"Deneme sürümünde dışa aktarma en fazla {ExportMaxRows} satır sınırı; abonelikle kalkar. Filtreyi daraltın.");

    public static Error ImportLimitExceeded(string tableDisplayName) => new(
        ImportLimitExceededCode,
        $"Deneme sürümünde içe aktarma tablo başına en fazla {ImportMaxRowsPerTable} satır sınırı ({tableDisplayName} tablosu aşıyor); abonelikle kalkar.");

    public static Error ClientQuotaExceeded() => new(
        ClientQuotaExceededCode,
        $"Deneme sürümünde en fazla {MaxClients} müşteri kaydı sınırı doldu; abonelikle kalkar.");

    public static Error PetQuotaExceeded() => new(
        PetQuotaExceededCode,
        $"Deneme sürümünde en fazla {MaxPets} hasta kaydı sınırı doldu; abonelikle kalkar.");

    public static Error StorageQuotaExceeded() => new(
        StorageQuotaExceededCode,
        $"Deneme sürümünde dosya depolama {MaxStorageBytes / (1024 * 1024)} MB sınırı doldu; abonelikle kalkar.");
}
