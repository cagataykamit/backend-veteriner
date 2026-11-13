using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Global referans: evcil hayvan kürk rengi ya da deseni (<see cref="Kind"/>).
/// </summary>
public sealed class PetColor : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }
    public PetColorKind Kind { get; private set; } = PetColorKind.Color;

    private PetColor() { }

    public PetColor(string code, string name, int displayOrder = 0, PetColorKind kind = PetColorKind.Color)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Renk kodu boş olamaz.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Renk adı boş olamaz.", nameof(name));

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsActive = true;
        DisplayOrder = displayOrder;
        Kind = kind;
    }

    public void Update(string code, string name, int displayOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Renk kodu boş olamaz.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Renk adı boş olamaz.", nameof(name));

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }
}
