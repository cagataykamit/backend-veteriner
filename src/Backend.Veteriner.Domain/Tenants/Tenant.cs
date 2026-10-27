using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// SaaS kiracısı (tenant) kök agregatı.
/// </summary>
public sealed class Tenant : AggregateRoot
{
    /// <summary>
    /// Platform yöneticilerinin bağlandığı, içi boş tutulan kiracı adı (<c>create-platform-admin</c>).
    /// Müşteri kiracısı bu adı alamaz (<see cref="IsReservedName"/>).
    /// </summary>
    public const string PlatformTenantName = "Platform";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private Tenant() { }

    public Tenant(string name, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant adı boş olamaz.", nameof(name));

        Name = name.Trim();
        IsActive = true;
        CreatedAtUtc = utcNow;
    }

    public static bool IsReservedName(string? name)
        => string.Equals(name?.Trim(), PlatformTenantName, StringComparison.OrdinalIgnoreCase);

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant adı boş olamaz.", nameof(name));
        Name = name.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
