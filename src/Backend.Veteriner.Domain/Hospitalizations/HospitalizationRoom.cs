using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Hospitalizations;

/// <summary>
/// Yatış odası/kafesi; bir bölümün (<see cref="HospitalizationWard"/>) altındadır ve bölümle aynı kliniğe aittir.
/// Aktif odalar arasında ad bölüm içinde benzersizdir. Oda bölümler arası taşınmaz.
/// </summary>
public sealed class HospitalizationRoom : IClinicScoped, ITenantOwned
{
    public const int NameMaxLength = HospitalizationWard.NameMaxLength;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid WardId { get; private set; }
    public string Name { get; private set; } = default!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private HospitalizationRoom() { }

    public static HospitalizationRoom Create(HospitalizationWard ward, string name, int sortOrder, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(ward);

        return new HospitalizationRoom
        {
            TenantId = ward.TenantId,
            ClinicId = ward.ClinicId,
            WardId = ward.Id,
            Name = HospitalizationWard.NormalizeName(name),
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = null,
        };
    }

    public void Update(string name, int sortOrder, DateTime utcNow)
    {
        Name = HospitalizationWard.NormalizeName(name);
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
}
