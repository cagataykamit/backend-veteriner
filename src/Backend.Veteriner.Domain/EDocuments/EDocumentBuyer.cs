using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.EDocuments;

/// <summary>
/// Belgenin alıcısı; belge anında müşterinin fatura kimliğinden kopyalanır, sonradan değişmez (ADR-036 Dilim 4).
/// Kimlik numarası tam saklanır (yeniden gönderimde gerekir), okuma yüzeyinde maskeli döner.
/// </summary>
public sealed class EDocumentBuyer
{
    public const int MaxNameLength = 300;

    public EDocumentBuyerKind Kind { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>Kurumsalda VKN (veya şahıs şirketi TCKN'si), bireyselde TCKN; bilinmeyen alıcıda yer tutucu.</summary>
    public string IdentityNumber { get; private set; } = default!;
    public bool IsUnknownBuyer { get; private set; }
    public string? TaxOffice { get; private set; }
    public string? Province { get; private set; }
    public string? District { get; private set; }
    public string? AddressLine { get; private set; }
    public string? Email { get; private set; }

    private EDocumentBuyer() { }

    public static Result<EDocumentBuyer> Corporate(
        string name, string? taxNumber, string? taxOffice, string? province, string? district, string? addressLine, string? email)
    {
        var identity = TurkishTaxIdentity.Normalize(taxNumber);
        if (identity is null || !TurkishTaxIdentity.IsValidTaxpayerIdentity(identity))
            return Result<EDocumentBuyer>.Failure("EDocuments.BuyerIdentityMissing", "Kurumsal alıcının geçerli vergi numarası eksik.");

        return Build(EDocumentBuyerKind.Corporate, name, identity, isUnknown: false, taxOffice, province, district, addressLine, email);
    }

    /// <summary>
    /// TCKN yoksa bilinmeyen alıcı yer tutucusu kullanılır (<see cref="EDocumentRules.UnknownBuyerNationalId"/>). Ad verilmişse ad ve
    /// soyad birlikte olmalıdır: e-Arşiv / e-SMM'de kişi alıcı ad + soyad ister (Nilvera test ortamı tek kelimelik adı reddetti,
    /// 2026-11-12); belge alıcı bilgisini kopyaladığı için sonradan düzeltmek yeniden denemeyle olmaz, kesmeden önce yakalanır.
    /// </summary>
    public static Result<EDocumentBuyer> Individual(
        string? name, string? nationalId, string? province, string? district, string? addressLine, string? email)
    {
        if (!string.IsNullOrWhiteSpace(name) && !HasFirstAndLastName(name))
            return Result<EDocumentBuyer>.Failure("EDocuments.BuyerIdentityMissing", "Bireysel alıcının adı ve soyadı birlikte yazılmalıdır.");

        var identity = TurkishTaxIdentity.Normalize(nationalId);
        if (identity is not null && !TurkishTaxIdentity.IsValidNationalId(identity))
            identity = null;

        var unknown = identity is null;
        var displayName = string.IsNullOrWhiteSpace(name) ? EDocumentRules.UnknownBuyerName : name;
        return Build(
            EDocumentBuyerKind.Individual,
            displayName,
            identity ?? EDocumentRules.UnknownBuyerNationalId,
            unknown,
            taxOffice: null,
            province,
            district,
            addressLine,
            email);
    }

    /// <summary>Kişi adında en az iki sözcük (ad + soyad) var mı.</summary>
    public static bool HasFirstAndLastName(string name)
        => name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= 2;

    public static EDocumentBuyer UnknownIndividual()
        => Individual(EDocumentRules.UnknownBuyerName, null, null, null, null, null).Value!;

    private static Result<EDocumentBuyer> Build(
        EDocumentBuyerKind kind, string name, string identity, bool isUnknown,
        string? taxOffice, string? province, string? district, string? addressLine, string? email)
    {
        var text = name.Trim();
        if (text.Length == 0)
            return Result<EDocumentBuyer>.Failure("EDocuments.Validation", "Alıcı adı zorunludur.");
        if (text.Length > MaxNameLength)
            text = text[..MaxNameLength];

        return Result<EDocumentBuyer>.Success(new EDocumentBuyer
        {
            Kind = kind,
            Name = text,
            IdentityNumber = identity,
            IsUnknownBuyer = isUnknown,
            TaxOffice = Clean(taxOffice),
            Province = Clean(province),
            District = Clean(district),
            AddressLine = Clean(addressLine),
            Email = Clean(email),
        });
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
