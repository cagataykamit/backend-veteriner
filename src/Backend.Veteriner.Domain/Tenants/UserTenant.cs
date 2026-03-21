using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Kullanıcının bir kiracıdaki üyeliğini temsil eder. Login/refresh sırasında yetki doğrulaması için kaynak.
/// </summary>
public sealed class UserTenant
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public User? User { get; private set; }
    public Tenant? Tenant { get; private set; }

    private UserTenant() { }

    public UserTenant(Guid userId, Guid tenantId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId geçersiz.", nameof(userId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        UserId = userId;
        TenantId = tenantId;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
