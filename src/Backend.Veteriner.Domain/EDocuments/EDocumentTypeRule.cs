using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Domain.EDocuments;

/// <summary>
/// Belge türü kuralı tek yerde (ADR-036 ilke 2, AK-3 c): klinik mükellef türü × satır türü × alıcı e-Fatura mükellefi mi.
/// Serbest meslek her satırda e-SMM; şirket her satırda fatura; karma klinikte hizmet e-SMM, ürün fatura.
/// Fatura = alıcı e-Fatura mükellefiyse e-Fatura, değilse e-Arşiv.
/// </summary>
public static class EDocumentTypeRule
{
    /// <summary>Satır e-SMM mi yoksa fatura (e-Arşiv / e-Fatura) mı; mükellef sorgusundan önce gruplamak için.</summary>
    public static bool IsSelfEmployedReceipt(ClinicTaxpayerType taxpayerType, ChargeLineKind lineKind)
        => taxpayerType switch
        {
            ClinicTaxpayerType.SelfEmployed => true,
            ClinicTaxpayerType.Company => false,
            ClinicTaxpayerType.Both => lineKind == ChargeLineKind.Service,
            _ => throw new ArgumentOutOfRangeException(nameof(taxpayerType), taxpayerType, null),
        };

    public static EDocumentType Resolve(ClinicTaxpayerType taxpayerType, ChargeLineKind lineKind, bool buyerIsEInvoiceUser)
    {
        if (IsSelfEmployedReceipt(taxpayerType, lineKind))
            return EDocumentType.ESmm;
        return buyerIsEInvoiceUser ? EDocumentType.EInvoice : EDocumentType.EArchive;
    }
}
