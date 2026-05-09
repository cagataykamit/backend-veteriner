using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Products;

/// <summary>Kiracıya özgü ürün kategorisi (tenant-wide; klinik taşımaz).</summary>
public sealed class ProductCategory : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public ICollection<Product> Products { get; private set; } = new List<Product>();

    private ProductCategory() { }

    public ProductCategory(Guid tenantId, string name, string? description = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Kategori adı boş olamaz.", nameof(name));

        TenantId = tenantId;
        Name = name.Trim();
        Description = NormalizeOptional(description);
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
