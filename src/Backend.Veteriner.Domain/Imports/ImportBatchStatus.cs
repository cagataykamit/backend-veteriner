namespace Backend.Veteriner.Domain.Imports;

/// <summary>
/// İçe aktarma partisinin yaşam döngüsü. Geçişler yalnız <see cref="ImportBatch"/> metotlarıyla yapılır.
/// Sayısal değerler veritabanı filtreli indekslerinde kullanıldığından değiştirilmez.
/// </summary>
public enum ImportBatchStatus
{
    Uploaded = 1,
    Mapped = 2,
    Validating = 3,
    Validated = 4,
    ValidationFailed = 5,
    Committing = 6,
    Committed = 7,
    CommitFailed = 8,
    RollingBack = 9,
    RolledBack = 10,
    PartiallyRolledBack = 11,
    RollbackFailed = 12,
    Discarded = 13,
    Expired = 14,
}

/// <summary>Durum kümeleri: taslak sınırı, kiracı başına tek aktif iş ve içerik özeti benzersizliği bu kümelerden okunur.</summary>
public static class ImportBatchStatusSets
{
    /// <summary>Henüz uygulanmamış parti; açık taslak sınırına sayılır.</summary>
    public static readonly IReadOnlyList<ImportBatchStatus> Draft =
    [
        ImportBatchStatus.Uploaded,
        ImportBatchStatus.Mapped,
        ImportBatchStatus.Validating,
        ImportBatchStatus.Validated,
        ImportBatchStatus.ValidationFailed,
    ];

    /// <summary>Kullanıcının silebildiği veya süresi dolabilen taslak (arka plan işi sürmeyen).</summary>
    public static readonly IReadOnlyList<ImportBatchStatus> IdleDraft =
    [
        ImportBatchStatus.Uploaded,
        ImportBatchStatus.Mapped,
        ImportBatchStatus.Validated,
        ImportBatchStatus.ValidationFailed,
    ];

    /// <summary>Arka plan işi süren durumlar; kiracıda aynı anda en fazla bir parti (veritabanı filtreli benzersiz indeksi).</summary>
    public static readonly IReadOnlyList<ImportBatchStatus> ActiveJob =
    [
        ImportBatchStatus.Validating,
        ImportBatchStatus.Committing,
        ImportBatchStatus.RollingBack,
    ];

    /// <summary>Doğrulaması bitmiş (satır sonuçları okunabilir) durumlar; eşleme değişince parti <c>Mapped</c>'a düşüp çıkar.</summary>
    public static readonly IReadOnlyList<ImportBatchStatus> ValidationResultAvailable =
    [
        ImportBatchStatus.Validated,
        ImportBatchStatus.ValidationFailed,
        ImportBatchStatus.Committing,
        ImportBatchStatus.Committed,
        ImportBatchStatus.CommitFailed,
        ImportBatchStatus.RollingBack,
        ImportBatchStatus.RolledBack,
        ImportBatchStatus.PartiallyRolledBack,
        ImportBatchStatus.RollbackFailed,
    ];

    /// <summary>
    /// Satır verisi saklama süresi (K9) sonunda temizlenen, işi bitmiş durumlar. <c>Discarded</c> ve <c>Expired</c>
    /// partinin satırları durum değişirken silinir; geri alınabilir başarısız durumlar da işlem bittiği için sayılır
    /// (geri alma yalnız satır durumu ve hedef kimliği kullanır, temizlenmiş partide de çalışır).
    /// </summary>
    public static readonly IReadOnlyList<ImportBatchStatus> PayloadRetained =
    [
        ImportBatchStatus.Committed,
        ImportBatchStatus.CommitFailed,
        ImportBatchStatus.RolledBack,
        ImportBatchStatus.PartiallyRolledBack,
        ImportBatchStatus.RollbackFailed,
    ];

    /// <summary>Geri almanın başlatılabildiği durumlar (geçiş tablosu); başarısız geri alma yeniden denenebilir.</summary>
    public static readonly IReadOnlyList<ImportBatchStatus> RollbackableFrom =
    [
        ImportBatchStatus.Committed,
        ImportBatchStatus.CommitFailed,
        ImportBatchStatus.PartiallyRolledBack,
        ImportBatchStatus.RollbackFailed,
    ];

    /// <summary>İçerik özetini serbest bırakan durumlar: aynı dosya yeniden yüklenebilir.</summary>
    public static readonly IReadOnlyList<ImportBatchStatus> ContentHashReleased =
    [
        ImportBatchStatus.CommitFailed,
        ImportBatchStatus.RolledBack,
        ImportBatchStatus.Discarded,
        ImportBatchStatus.Expired,
    ];
}
