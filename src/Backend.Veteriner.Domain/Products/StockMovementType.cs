namespace Backend.Veteriner.Domain.Products;

/// <summary>
/// Stok hareket yönü ve tipi. Miktar her zaman pozitif; işaret <see cref="StockMovementType"/> ile yorumlanır.
/// </summary>
public enum StockMovementType
{
    Initial = 0,
    In = 1,
    Out = 2,
    Adjustment = 3
}
