using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Global referans: bir türe bağlı ırk. Tenant’a bağlı değildir.
/// </summary>
public sealed class Breed : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SpeciesId { get; private set; }
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }

    /// <summary>Liste/detay projeksiyonları için (EF ilişkisi).</summary>
    public Species? Species { get; private set; }

    private Breed() { }

    public Breed(Guid speciesId, string name)
    {
        if (speciesId == Guid.Empty)
            throw new ArgumentException("SpeciesId geçersiz.", nameof(speciesId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Irk adı boş olamaz.", nameof(name));

        SpeciesId = speciesId;
        Name = name.Trim();
        IsActive = true;
    }

    public void Update(string name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Irk adı boş olamaz.", nameof(name));
        Name = name.Trim();
        IsActive = isActive;
    }
}
