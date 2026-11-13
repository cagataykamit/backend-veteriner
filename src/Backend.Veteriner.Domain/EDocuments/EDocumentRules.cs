using System.Linq.Expressions;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Domain.EDocuments;

/// <summary>
/// e-belge mevzuat değerleri tek yerde (ADR-036 ilke 2). Değerler <b>DOĞRULANMADI</b> (keşif ve görüşme paketi);
/// mevzuat değişirse yalnız burası değişir.
/// </summary>
public static class EDocumentRules
{
    /// <summary>Kesilmiş belgenin iptal edilebileceği gün sayısı (7/8 gün kaynakları çelişkili; sağlayıcı ayrıca reddedebilir).</summary>
    public const int CancelWindowDays = 8;

    /// <summary>
    /// Ticari e-Faturada alıcının sistem üzerinden kabul / red yanıtı verebileceği gün sayısı (kaynaklar 7 / 8 gün diyor;
    /// DOĞRULANMADI). Bu süre (+ <see cref="BuyerAnswerGraceDays"/>) dolup yanıt gelmezse belge kabul edilmiş sayılır.
    /// </summary>
    public const int BuyerAnswerWindowDays = 8;

    /// <summary>Yanıt süresinden sonra sağlayıcıya sorulmaya devam edilen gün (geç gelen yanıtı kaçırmamak için).</summary>
    public const int BuyerAnswerGraceDays = 1;

    /// <summary>Kimliğini vermeyen bireysel alıcı için kullanılan TCKN yer tutucusu.</summary>
    public const string UnknownBuyerNationalId = "11111111111";

    /// <summary>Kimliği bilinmeyen alıcının adı.</summary>
    public const string UnknownBuyerName = "Nihai Tüketici";

    /// <summary>Tek belgeye bağlanabilecek en çok ücret.</summary>
    public const int MaxChargesPerDocument = 50;

    /// <summary>
    /// Belgeye girebilecek ücret: eski programdan devreden bakiye ücreti (ADR-048 T3) girmez; belgesi eski programın konusudur.
    /// Belgesiz listesi, hatırlatma sayacı, otomatik kesme ve ödemeden kesme bu kuralla süzer; elle seçimde reddedilir.
    /// </summary>
    public static readonly Expression<Func<Charge, bool>> IsDocumentableCharge = c => c.ImportBatchId == null;

    private static readonly Func<Charge, bool> IsDocumentableChargeCompiled = IsDocumentableCharge.Compile();

    /// <summary>Bellekteki ücret için <see cref="IsDocumentableCharge"/>.</summary>
    public static bool IsDocumentable(Charge charge) => IsDocumentableChargeCompiled(charge);

    public const int MaxNoteLength = 500;
    public const int MaxReasonLength = 500;

    /// <summary>Serbest meslek stopajı yalnız kurumsal alıcıya (vergi kesintisi yapmakla yükümlü) kesilen e-SMM'de uygulanır.</summary>
    public static bool AppliesWithholding(EDocumentType type, EDocumentBuyerKind buyerKind)
        => type == EDocumentType.ESmm && buyerKind == EDocumentBuyerKind.Corporate;
}
