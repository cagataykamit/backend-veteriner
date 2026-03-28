using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Pets;

/// <summary>
/// Kiracı ve müşteriye bağlı evcil hayvan kaydı.
/// Tür <see cref="SpeciesId"/> ile global <see cref="Catalog.Species"/> tablosuna bağlıdır.
/// Irk şimdilik serbest metin (<see cref="Breed"/>); ileride opsiyonel <c>BreedId</c> eklenebilir.
/// </summary>
public sealed class Pet : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClientId { get; private set; }
    public string Name { get; private set; } = default!;
    public Guid SpeciesId { get; private set; }
    public string? Breed { get; private set; }

    /// <summary>Global ırk kaydı (FK); serbest metin <see cref="Breed"/> (string) ile birlikte kullanılabilir.</summary>
    public Guid? BreedId { get; private set; }

    public PetGender? Gender { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    /// <summary>Okuma/projeksiyon için (EF ilişkisi).</summary>
    public Species? Species { get; private set; }

    /// <summary>Okuma/projeksiyon için (EF ilişkisi).</summary>
    public Breed? BreedRef { get; private set; }

    private Pet() { }

    public Pet(
        Guid tenantId,
        Guid clientId,
        string name,
        Guid speciesId,
        string? breed = null,
        DateOnly? birthDate = null,
        Guid? breedId = null,
        PetGender? gender = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clientId == Guid.Empty)
            throw new ArgumentException("ClientId geçersiz.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Hayvan adı boş olamaz.", nameof(name));
        if (speciesId == Guid.Empty)
            throw new ArgumentException("SpeciesId geçersiz.", nameof(speciesId));

        TenantId = tenantId;
        ClientId = clientId;
        Name = name.Trim();
        SpeciesId = speciesId;
        Breed = string.IsNullOrWhiteSpace(breed) ? null : breed.Trim();
        BirthDate = birthDate;
        BreedId = breedId;
        Gender = gender;
    }

    public void UpdateDetails(
        string name,
        Guid speciesId,
        string? breed,
        DateOnly? birthDate,
        Guid? breedId = null,
        PetGender? gender = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Hayvan adı boş olamaz.", nameof(name));
        if (speciesId == Guid.Empty)
            throw new ArgumentException("SpeciesId geçersiz.", nameof(speciesId));
        Name = name.Trim();
        SpeciesId = speciesId;
        Breed = string.IsNullOrWhiteSpace(breed) ? null : breed.Trim();
        BirthDate = birthDate;
        BreedId = breedId;
        Gender = gender;
    }
}
