namespace Backend.Veteriner.Application.EDocuments;

/// <summary>Bekleyen belgeleri (asenkron sonuç, geçici hata, iptal) sağlayıcıya soran arka plan işi ayarları (<c>EDocuments:Processor</c>).</summary>
public sealed class EDocumentProcessorOptions
{
    public const string SectionName = "EDocuments:Processor";

    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 15;
    public int BatchSize { get; init; } = 20;

    /// <summary>Belge başına arka plan kontrolü üst sınırı (artan aralıkla ~1 gün); aşılınca belge hatalı olur.</summary>
    public int MaxAttempts { get; init; } = 60;
}
