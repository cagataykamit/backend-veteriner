using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Domain.Charges;

/// <summary>Ücret satır türü (ADR-036): belge türü kuralının girdisi. Varsayılan ve mevcut kayıtlar <see cref="Service"/>.</summary>
public enum ChargeLineKind
{
    Service = 0,
    Product = 1
}

/// <summary>Satır türünün katalogdan türetilmesi; kural yalnızca burada yaşar.</summary>
public static class ChargeLineKinds
{
    /// <summary>Hizmet kataloğu kalemi hizmettir; ilaç, aşı ve diğer ürünler üründür.</summary>
    public static ChargeLineKind FromProductKind(ProductKind kind)
        => kind == ProductKind.Service ? ChargeLineKind.Service : ChargeLineKind.Product;
}
