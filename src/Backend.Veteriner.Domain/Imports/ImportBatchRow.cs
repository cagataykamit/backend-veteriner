namespace Backend.Veteriner.Domain.Imports;

/// <summary>
/// Partinin bir kaynak satırı (hazırlık verisi). Ürün tablolarına onaydan önce hiçbir şey yazılmaz; satır sonucu ve
/// oluşturulan/eşleşen kayıt kimliği kaydın kendisiyle aynı transaction'da yazılır. <see cref="SourceJson"/> ve
/// <see cref="NormalizedJson"/> kişisel veri içerir; saklama süresi sonunda temizlenir (null olur).
/// </summary>
public sealed class ImportBatchRow
{
    public const int MaxTargetEntityTypeLength = 32;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid BatchId { get; private set; }
    public ImportTable Table { get; private set; }

    /// <summary>Kaynak dosyadaki satır numarası (başlık satırı dahil sayım, kullanıcıya gösterilir).</summary>
    public int RowNumber { get; private set; }

    public string? SourceJson { get; private set; }
    public string? NormalizedJson { get; private set; }
    public ImportRowValidationStatus ValidationStatus { get; private set; }
    public string? IssuesJson { get; private set; }
    public ImportRowApplyStatus ApplyStatus { get; private set; }
    public string? TargetEntityType { get; private set; }
    public Guid? TargetEntityId { get; private set; }

    /// <summary>Satır mevcut bir kayıtla eşleşti (yeni kayıt oluşmadı); geri almada silinmez.</summary>
    public bool Matched { get; private set; }

    public DateTime? AppliedAtUtc { get; private set; }

    private ImportBatchRow() { }

    /// <summary>
    /// Doğrulama (kuru çalıştırma) sonucunu yazar; önceki sonucun yerini alır. Uygulama durumu ve hedef kayıt
    /// değişmez (B4 yazar).
    /// </summary>
    public void ApplyValidation(ImportRowValidationStatus status, string? issuesJson, string normalizedJson)
    {
        if (status == ImportRowValidationStatus.Pending)
            throw new ArgumentOutOfRangeException(nameof(status), "Doğrulama sonucu Pending olamaz.");

        ValidationStatus = status;
        IssuesJson = issuesJson;
        NormalizedJson = normalizedJson;
    }

    /// <summary>Saklama süresi sonunda kişisel veriyi boşaltır; satır kimliği, durumları ve hedef kayıt kimliği kalır.</summary>
    public void PurgePayload()
    {
        SourceJson = null;
        NormalizedJson = null;
        IssuesJson = null;
    }

    public static ImportBatchRow Create(Guid batchId, ImportTable table, int rowNumber, string sourceJson, string? normalizedJson)
    {
        if (batchId == Guid.Empty)
            throw new ArgumentException("BatchId geçersiz.", nameof(batchId));
        if (!Enum.IsDefined(table))
            throw new ArgumentOutOfRangeException(nameof(table));
        if (rowNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(rowNumber), "Satır numarası 1'den küçük olamaz.");
        if (string.IsNullOrWhiteSpace(sourceJson))
            throw new ArgumentException("Kaynak satır boş olamaz.", nameof(sourceJson));

        return new ImportBatchRow
        {
            BatchId = batchId,
            Table = table,
            RowNumber = rowNumber,
            SourceJson = sourceJson,
            NormalizedJson = normalizedJson,
            ValidationStatus = ImportRowValidationStatus.Pending,
            ApplyStatus = ImportRowApplyStatus.Pending,
        };
    }
}
