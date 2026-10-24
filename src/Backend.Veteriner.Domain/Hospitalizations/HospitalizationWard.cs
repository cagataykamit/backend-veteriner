using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Hospitalizations;

/// <summary>
/// Klinik bazlı yatış bölümü (ör. "Yoğun bakım", "Kedi servisi"); odalar (<see cref="HospitalizationRoom"/>) bu bölümün
/// altındadır. Kapasite/çakışma kuralı yoktur. Aktif bölümler arasında ad klinik içinde benzersizdir; pasif bölüm yeni
/// yatışa atanamaz, mevcut yatışlar etkilenmez.
/// </summary>
public sealed class HospitalizationWard : IClinicScoped
{
    public const int NameMaxLength = 100;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public string Name { get; private set; } = default!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private HospitalizationWard() { }

    public static HospitalizationWard Create(Guid tenantId, Guid clinicId, string name, int sortOrder, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        return new HospitalizationWard
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            Name = NormalizeName(name),
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = null,
        };
    }

    public void Update(string name, int sortOrder, DateTime utcNow)
    {
        Name = NormalizeName(name);
        SortOrder = sortOrder;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Aynı duruma geçiş değişiklik sayılmaz (<c>false</c> döner); çağıran kaydı yazmaz.</summary>
    public bool SetActive(bool isActive, DateTime utcNow)
    {
        if (IsActive == isActive)
            return false;

        IsActive = isActive;
        UpdatedAtUtc = utcNow;
        return true;
    }

    internal static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ad boş olamaz.", nameof(name));

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Ad en fazla {NameMaxLength} karakter olabilir.", nameof(name));

        return trimmed;
    }
}
