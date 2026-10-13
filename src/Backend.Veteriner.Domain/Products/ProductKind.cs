namespace Backend.Veteriner.Domain.Products;

/// <summary>Ürün sınıfı: kayıt defterlerini (aşı, ilaç) ayırır ve parti zorunluluğunu belirler.</summary>
public enum ProductKind
{
    Other = 0,
    Medicine = 1,
    Vaccine = 2
}
