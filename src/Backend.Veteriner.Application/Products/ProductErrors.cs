using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Products;

/// <summary>Ürün kartı barkod kuralı (TIER-006, docs/MOBILE_BARCODE_API_CONTRACT.md); HTTP eşlemesi <c>ResultExtensions</c> içinde koddan türer.</summary>
internal static class ProductErrors
{
    public const string DuplicateBarcodeCode = "Products.DuplicateBarcode";

    /// <summary>409: barkod bu kiracıda başka bir üründe (pasif dahil) kayıtlı.</summary>
    public static Error DuplicateBarcode(string? otherProductName)
        => new(
            DuplicateBarcodeCode,
            otherProductName is null
                ? "Bu barkod başka bir üründe kayıtlı."
                : $"Bu barkod \"{otherProductName}\" ürününde kayıtlı.");

    /// <summary>Eşzamanlı iki kayıtta ön denetimi geçen ikinci yazma benzersiz indekse takılır.</summary>
    public static bool IsDuplicateBarcodeViolation(DbUpdateException ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains("IX_Products_TenantId_Barcode", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
