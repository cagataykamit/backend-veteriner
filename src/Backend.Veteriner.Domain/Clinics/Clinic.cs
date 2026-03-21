using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Kiracıya bağlı veteriner kliniği.
/// </summary>
public sealed class Clinic : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string City { get; private set; } = default!;
    public bool IsActive { get; private set; }

    private Clinic() { }

    public Clinic(Guid tenantId, string name, string city)
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
        IsActive = true;
    }

    public void UpdateDetails(string name, string city)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Klinik adı boş olamaz.", nameof(name));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("Şehir bilgisi boş olamaz.", nameof(city));
        Name = name.Trim();
        City = city.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
