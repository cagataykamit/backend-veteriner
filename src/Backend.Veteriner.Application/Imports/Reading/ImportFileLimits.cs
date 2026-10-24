using Backend.Veteriner.Application.Tenants;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>
/// Dosya okuma tavanları (IMPORT_API_CONTRACT.md §4.1). Tavanlar dosya belleğe açılmadan önce ve okuma sırasında
/// denetlenir; <c>Imports:Limits</c> yapılandırmasıyla değiştirilebilir. Deneme kiracısı için <see cref="ForTrial"/>.
/// </summary>
public sealed class ImportFileLimits
{
    /// <summary>Deneme kiracısı tavanları (TRIAL-004): yalnız <see cref="MaxRowsPerTable"/> düşer, aşımda <c>Trial.ImportLimitExceeded</c>.</summary>
    public ImportFileLimits ForTrial() => new()
    {
        MaxFileBytes = MaxFileBytes,
        MaxRowsPerTable = Math.Min(MaxRowsPerTable, TrialLimits.ImportMaxRowsPerTable),
        MaxTotalRows = MaxTotalRows,
        MaxColumns = MaxColumns,
        MaxCellChars = MaxCellChars,
        MaxSheets = MaxSheets,
        MaxZipEntries = MaxZipEntries,
        MaxUncompressedBytes = MaxUncompressedBytes,
        IsTrial = true,
    };

    /// <summary>Tablo satır tavanı deneme sınırından geliyor mu (yapılandırmadan bağlanmaz).</summary>
    public bool IsTrial { get; private init; }

    public const string SectionName = "Imports:Limits";

    /// <summary>Üç dosyalı CSV yüklemesi için istek gövdesi üst sınırı (taşıma katmanında 413).</summary>
    public const long MaxRequestBytes = 33L * 1024 * 1024;

    public int MaxFileBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxRowsPerTable { get; set; } = 10_000;
    public int MaxTotalRows { get; set; } = 25_000;
    public int MaxColumns { get; set; } = 60;
    public int MaxCellChars { get; set; } = 10_000;
    public int MaxSheets { get; set; } = 20;

    /// <summary>XLSX zip girişi sayısı üst sınırı.</summary>
    public int MaxZipEntries { get; set; } = 1_000;

    /// <summary>XLSX içindeki tüm parçaların <b>gerçek</b> açılmış boyut toplamı (zip bombası).</summary>
    public long MaxUncompressedBytes { get; set; } = 100L * 1024 * 1024;
}
