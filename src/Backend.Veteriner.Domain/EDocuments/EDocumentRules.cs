namespace Backend.Veteriner.Domain.EDocuments;

/// <summary>
/// e-belge mevzuat değerleri tek yerde (ADR-036 ilke 2). Değerler <b>DOĞRULANMADI</b> (keşif ve görüşme paketi);
/// mevzuat değişirse yalnız burası değişir.
/// </summary>
public static class EDocumentRules
{
    /// <summary>Kesilmiş belgenin iptal edilebileceği gün sayısı (7/8 gün kaynakları çelişkili; sağlayıcı ayrıca reddedebilir).</summary>
    public const int CancelWindowDays = 8;

    /// <summary>Kimliğini vermeyen bireysel alıcı için kullanılan TCKN yer tutucusu.</summary>
    public const string UnknownBuyerNationalId = "11111111111";

    /// <summary>Kimliği bilinmeyen alıcının adı.</summary>
    public const string UnknownBuyerName = "Nihai Tüketici";

    /// <summary>Tek belgeye bağlanabilecek en çok ücret.</summary>
    public const int MaxChargesPerDocument = 50;

    public const int MaxNoteLength = 500;
    public const int MaxReasonLength = 500;

    /// <summary>Serbest meslek stopajı yalnız kurumsal alıcıya (vergi kesintisi yapmakla yükümlü) kesilen e-SMM'de uygulanır.</summary>
    public static bool AppliesWithholding(EDocumentType type, EDocumentBuyerKind buyerKind)
        => type == EDocumentType.ESmm && buyerKind == EDocumentBuyerKind.Corporate;
}
