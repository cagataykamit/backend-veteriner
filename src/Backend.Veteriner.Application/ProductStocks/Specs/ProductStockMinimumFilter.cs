using System.Linq.Expressions;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.ProductStocks.Specs;

/// <summary>
/// Düşük stok süzgeci tek yerde: eşik, toplama değil <b>kullanılabilir</b> miktara (toplam − süresi geçmiş parti kalanı) bakar.
/// </summary>
internal static class ProductStockMinimumFilter
{
    public static Expression<Func<ProductStock, bool>> Create(bool isBelowMinimum, DateOnly today)
        => isBelowMinimum
            ? s => s.QuantityOnHand
                   - (s.Product!.StockBatches
                       .Where(b => b.ClinicId == s.ClinicId && b.ExpiryDate != null && b.ExpiryDate < today)
                       .Sum(b => (decimal?)b.RemainingQuantity) ?? 0)
                   < s.MinimumStockLevel
            : s => s.QuantityOnHand
                   - (s.Product!.StockBatches
                       .Where(b => b.ClinicId == s.ClinicId && b.ExpiryDate != null && b.ExpiryDate < today)
                       .Sum(b => (decimal?)b.RemainingQuantity) ?? 0)
                   >= s.MinimumStockLevel;
}
