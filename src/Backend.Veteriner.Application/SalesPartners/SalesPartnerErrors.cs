using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.SalesPartners;

/// <summary>Satış ortağı ve indirim kodu uçlarının hata kodları (HTTP eşlemesi <c>ResultExtensions</c> içinde koddan türer).</summary>
internal static class SalesPartnerErrors
{
    public static readonly Error PartnerNotFound = new("SalesPartners.NotFound", "Satış ortağı bulunamadı.");

    public static readonly Error CodeNotFound = new("DiscountCodes.NotFound", "İndirim kodu bulunamadı.");

    public static readonly Error TenantNotFound = new("Tenants.NotFound", "Kiracı bulunamadı.");

    /// <summary>409: aynı normalize kod zaten var.</summary>
    public static readonly Error DuplicateCode = new("DiscountCodes.DuplicateCode", "Bu kod zaten kullanılıyor.");

    /// <summary>409: kiracı zaten bir kod kullanmış (ADR-037 madde 5: tek kod).</summary>
    public static readonly Error DuplicateRedemption =
        new("DiscountCodes.DuplicateRedemption", "Bu kurum için zaten bir ortak kodu kullanılmış.");

    /// <summary>409: ilk ücretli tahsilattan sonra kod girilemez.</summary>
    public static readonly Error ApplyNotOpen =
        new("DiscountCodes.ApplyNotOpen", "Ortak kodu yalnız kayıtta ya da ilk ücretli ödemeden önce girilebilir.");

    public static readonly Error AccessDenied = new("Tenants.AccessDenied", "Bu kiracı için işlem yetkisi yok.");

    public static bool IsUniqueViolation(DbUpdateException ex, string indexName)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
