using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clients;

/// <summary>
/// Kiracıya bağlı müşteri (hayvan sahibi).
/// Telefon opsiyoneldir (ilk kayıtta yalnız ad ile açılabilir).
/// </summary>
public sealed class Client : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string FullName { get; private set; } = default!;
    public string? Phone { get; private set; }

    private Client() { }

    public Client(Guid tenantId, string fullName, string? phone = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Ad soyad boş olamaz.", nameof(fullName));

        TenantId = tenantId;
        FullName = fullName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public void UpdateDetails(string fullName, string? phone)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Ad soyad boş olamaz.", nameof(fullName));
        FullName = fullName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }
}
