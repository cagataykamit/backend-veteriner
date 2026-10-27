using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Klinik bazlı mükellef ayarları (ADR-036 Dilim 1): belge düzenleyen kimliği, adresi ve varsayılan KDV/stopaj oranları.
/// Tenant + Clinic benzersizdir; kayıt yoksa <see cref="TaxRatePercent.DefaultVatRate"/> geçerlidir.
/// Entegratör sağlayıcısı ve kimlik bilgisi bu kayıtta değildir (Dilim 3).
/// </summary>
public sealed class ClinicTaxpayerSettings : ITenantOwned
{
    public const int MaxTaxOfficeLength = 100;
    public const int MaxTaxpayerNameLength = 300;
    public const int MaxProvinceLength = 80;
    public const int MaxDistrictLength = 80;
    public const int MaxAddressLineLength = 500;

    private const string ValidationCode = "Clinics.TaxpayerSettings.Validation";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }

    /// <summary><c>null</c> = belirtilmemiş.</summary>
    public ClinicTaxpayerType? TaxpayerType { get; private set; }

    /// <summary>Mükellef VKN (10 hane) veya TCKN (11 hane).</summary>
    public string? TaxIdentityNumber { get; private set; }

    public string? TaxOffice { get; private set; }

    /// <summary>Unvan (şirket) veya ad soyad (serbest meslek).</summary>
    public string? TaxpayerName { get; private set; }

    public string? Province { get; private set; }
    public string? District { get; private set; }
    public string? AddressLine { get; private set; }

    /// <summary>Oranı boş ücret satırlarında geçerli KDV oranı (%).</summary>
    public decimal DefaultVatRate { get; private set; } = TaxRatePercent.DefaultVatRate;

    /// <summary>Stopaj oranı (%); serbest meslek için, varsayılan boş.</summary>
    public decimal? WithholdingRate { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Ayarı son kaydeden kullanıcı.</summary>
    public Guid UpdatedByUserId { get; private set; }

    private ClinicTaxpayerSettings() { }

    public static Result<ClinicTaxpayerSettings> Create(
        Guid tenantId,
        Guid clinicId,
        ClinicTaxpayerType? taxpayerType,
        string? taxIdentityNumber,
        string? taxOffice,
        string? taxpayerName,
        string? province,
        string? district,
        string? addressLine,
        decimal defaultVatRate,
        decimal? withholdingRate,
        Guid userId,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        var row = new ClinicTaxpayerSettings
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            CreatedAtUtc = utcNow,
        };
        var applied = row.Apply(
            taxpayerType, taxIdentityNumber, taxOffice, taxpayerName, province, district, addressLine,
            defaultVatRate, withholdingRate, userId);
        return applied.IsSuccess
            ? Result<ClinicTaxpayerSettings>.Success(row)
            : Result<ClinicTaxpayerSettings>.Failure(applied.Error);
    }

    public Result Update(
        ClinicTaxpayerType? taxpayerType,
        string? taxIdentityNumber,
        string? taxOffice,
        string? taxpayerName,
        string? province,
        string? district,
        string? addressLine,
        decimal defaultVatRate,
        decimal? withholdingRate,
        Guid userId,
        DateTime utcNow)
    {
        var applied = Apply(
            taxpayerType, taxIdentityNumber, taxOffice, taxpayerName, province, district, addressLine,
            defaultVatRate, withholdingRate, userId);
        if (applied.IsSuccess)
            UpdatedAtUtc = utcNow;
        return applied;
    }

    /// <summary>
    /// Tam değiştirme. Mükellef kimliği doluysa 10 hane geçerli VKN veya 11 hane geçerli TCKN olmalı;
    /// oranlar <see cref="TaxRatePercent"/> kuralına uyar. Kural yalnızca burada yaşar.
    /// </summary>
    private Result Apply(
        ClinicTaxpayerType? taxpayerType,
        string? taxIdentityNumber,
        string? taxOffice,
        string? taxpayerName,
        string? province,
        string? district,
        string? addressLine,
        decimal defaultVatRate,
        decimal? withholdingRate,
        Guid userId)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ValidationCode, "Kaydeden kullanıcı geçersiz.");
        if (taxpayerType is { } type && !Enum.IsDefined(type))
            return Result.Failure(ValidationCode, "Geçersiz mükellef türü.");

        var identity = TurkishTaxIdentity.Normalize(taxIdentityNumber);
        if (identity is not null && !TurkishTaxIdentity.IsValidTaxpayerIdentity(identity))
            return Result.Failure(ValidationCode, "Mükellef kimlik numarası geçerli bir VKN (10 hane) veya TCKN (11 hane) olmalıdır.");

        if (!TaxRatePercent.IsValid(defaultVatRate) || !TaxRatePercent.IsValidOptional(withholdingRate))
            return Result.Failure(ValidationCode, TaxRatePercent.InvalidMessage);

        var office = NormalizeOptional(taxOffice);
        var name = NormalizeOptional(taxpayerName);
        var provinceText = NormalizeOptional(province);
        var districtText = NormalizeOptional(district);
        var addressText = NormalizeOptional(addressLine);
        if (office is { Length: > MaxTaxOfficeLength }
            || name is { Length: > MaxTaxpayerNameLength }
            || provinceText is { Length: > MaxProvinceLength }
            || districtText is { Length: > MaxDistrictLength }
            || addressText is { Length: > MaxAddressLineLength })
            return Result.Failure(ValidationCode, "Mükellef bilgisi alanı izin verilen uzunluğu aşıyor.");

        TaxpayerType = taxpayerType;
        TaxIdentityNumber = identity;
        TaxOffice = office;
        TaxpayerName = name;
        Province = provinceText;
        District = districtText;
        AddressLine = addressText;
        DefaultVatRate = defaultVatRate;
        WithholdingRate = withholdingRate;
        UpdatedByUserId = userId;
        return Result.Success();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
