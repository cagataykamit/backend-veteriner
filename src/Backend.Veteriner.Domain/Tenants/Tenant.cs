using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// SaaS kiracısı (tenant) kök agregatı.
/// </summary>
public sealed class Tenant : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private Tenant() { }

    public Tenant(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant adı boş olamaz.", nameof(name));

        Name = name.Trim();
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant adı boş olamaz.", nameof(name));
        Name = name.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
