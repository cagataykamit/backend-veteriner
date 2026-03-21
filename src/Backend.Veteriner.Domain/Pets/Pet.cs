using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Pets;

/// <summary>
/// Kiracı ve müşteriye bağlı evcil hayvan kaydı.
/// Irk (Breed) bilinmiyorsa opsiyonel bırakılabilir.
/// </summary>
public sealed class Pet : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClientId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Species { get; private set; } = default!;
    public string? Breed { get; private set; }
    public DateOnly? BirthDate { get; private set; }

    private Pet() { }

    public Pet(Guid tenantId, Guid clientId, string name, string species, string? breed = null, DateOnly? birthDate = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clientId == Guid.Empty)
            throw new ArgumentException("ClientId geçersiz.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Hayvan adı boş olamaz.", nameof(name));
        if (string.IsNullOrWhiteSpace(species))
            throw new ArgumentException("Tür bilgisi boş olamaz.", nameof(species));

        TenantId = tenantId;
        ClientId = clientId;
        Name = name.Trim();
        Species = species.Trim();
        Breed = string.IsNullOrWhiteSpace(breed) ? null : breed.Trim();
        BirthDate = birthDate;
    }

    public void UpdateDetails(string name, string species, string? breed, DateOnly? birthDate)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Hayvan adı boş olamaz.", nameof(name));
        if (string.IsNullOrWhiteSpace(species))
            throw new ArgumentException("Tür bilgisi boş olamaz.", nameof(species));
        Name = name.Trim();
        Species = species.Trim();
        Breed = string.IsNullOrWhiteSpace(breed) ? null : breed.Trim();
        BirthDate = birthDate;
    }
}
