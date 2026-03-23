using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Global referans: evcil hayvan türü (köpek, kedi, …). Tenant’a bağlı değildir.
/// </summary>
public sealed class Species : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }

    private Species() { }

    public Species(string code, string name, int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Tür kodu boş olamaz.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tür adı boş olamaz.", nameof(name));

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsActive = true;
        DisplayOrder = displayOrder;
    }

    public void Update(string code, string name, int displayOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Tür kodu boş olamaz.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tür adı boş olamaz.", nameof(name));

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }
}
