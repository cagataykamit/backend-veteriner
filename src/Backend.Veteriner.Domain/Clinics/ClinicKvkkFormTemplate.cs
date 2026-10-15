using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Klinik başına yazdırılabilir KVKK şablon metni: açık rıza formu (ADR-021) veya aydınlatma metni (ADR-024).
/// Tenant + Clinic + <see cref="Kind"/> benzersizdir. Metni klinik yazar (hukuk danışmanının onayladığı metin);
/// yer tutucular istemcide doldurulur, sunucu ham metni saklar.
/// </summary>
public sealed class ClinicKvkkFormTemplate
{
    public const int MaxTextLength = 20_000;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public ClinicKvkkFormKind Kind { get; private set; }
    public string Text { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Metni son kaydeden kullanıcı.</summary>
    public Guid UpdatedByUserId { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private ClinicKvkkFormTemplate() { }

    public static Result<ClinicKvkkFormTemplate> Create(
        Guid tenantId,
        Guid clinicId,
        ClinicKvkkFormKind kind,
        string? text,
        Guid userId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("Şablon türü geçersiz.", nameof(kind));

        var row = new ClinicKvkkFormTemplate
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            Kind = kind,
            CreatedAtUtc = DateTime.UtcNow,
        };
        var applied = row.ApplyText(text, userId);
        return applied.IsSuccess
            ? Result<ClinicKvkkFormTemplate>.Success(row)
            : Result<ClinicKvkkFormTemplate>.Failure(applied.Error);
    }

    public Result UpdateText(string? text, Guid userId)
    {
        var applied = ApplyText(text, userId);
        if (applied.IsSuccess)
            UpdatedAtUtc = DateTime.UtcNow;
        return applied;
    }

    /// <summary>
    /// İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.
    /// </summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    /// <summary>Boş olamaz, <see cref="MaxTextLength"/> karakteri aşamaz; baştaki/sondaki boşluk kırpılır. Kural yalnızca burada yaşar.</summary>
    private Result ApplyText(string? text, Guid userId)
    {
        if (userId == Guid.Empty)
            return Result.Failure("Clinics.KvkkFormTemplate.Validation", "Kaydeden kullanıcı geçersiz.");

        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Failure("Clinics.KvkkFormTemplate.Validation", "Form metni boş olamaz.");
        if (trimmed.Length > MaxTextLength)
            return Result.Failure(
                "Clinics.KvkkFormTemplate.Validation",
                $"Form metni en fazla {MaxTextLength} karakter olabilir.");

        Text = trimmed;
        UpdatedByUserId = userId;
        return Result.Success();
    }
}
