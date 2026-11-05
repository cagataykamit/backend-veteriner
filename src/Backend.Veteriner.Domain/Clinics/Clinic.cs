using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Kiracıya bağlı veteriner kliniği.
/// </summary>
public sealed class Clinic : AggregateRoot, ITenantOwned
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string City { get; private set; } = default!;
    /// <summary>İlçe (isteğe bağlı); kısa konum satırında şehirle birlikte gösterilir.</summary>
    public string? District { get; private set; }
    public bool IsActive { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? Description { get; private set; }

    private Clinic() { }

    public Clinic(
        Guid tenantId,
        string name,
        string city,
        string? phone = null,
        string? email = null,
        string? address = null,
        string? description = null,
        string? district = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Klinik adı boş olamaz.", nameof(name));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("Şehir bilgisi boş olamaz.", nameof(city));

        TenantId = tenantId;
        Name = name.Trim();
        City = city.Trim();
        District = NormalizeOptional(district);
        IsActive = true;
        Phone = NormalizeOptional(phone);
        Email = NormalizeOptional(email);
        Address = NormalizeOptional(address);
        Description = NormalizeOptional(description);
    }

    public void UpdateDetails(
        string name,
        string city,
        string? phone,
        string? email,
        string? address,
        string? description,
        string? district)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Klinik adı boş olamaz.", nameof(name));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("Şehir bilgisi boş olamaz.", nameof(city));
        Name = name.Trim();
        City = city.Trim();
        District = NormalizeOptional(district);
        Phone = NormalizeOptional(phone);
        Email = NormalizeOptional(email);
        Address = NormalizeOptional(address);
        Description = NormalizeOptional(description);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
