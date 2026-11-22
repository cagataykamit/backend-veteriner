using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.DrugGuide;

/// <summary>
/// İlaç rehberinin global etken madde listesi (REF-001, ADR-049 K3). Kiracıya bağlı değildir, salt okunurdur.
/// Yazım varyantları tek maddeye <see cref="NormalizedName"/> ile eşlenir (kural <see cref="TurkishTextKey"/>).
/// </summary>
public sealed class ActiveIngredient : AggregateRoot
{
    public const int NameMaxLength = 200;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = default!;
    public string NormalizedName { get; private set; } = default!;

    private ActiveIngredient() { }

    public ActiveIngredient(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Etken madde adı boş olamaz.", nameof(name));
        if (name.Trim().Length > NameMaxLength)
            throw new ArgumentException($"Etken madde adı en fazla {NameMaxLength} karakter olabilir.", nameof(name));

        Name = name.Trim();
        NormalizedName = TurkishTextKey.Normalize(name);
    }
}
