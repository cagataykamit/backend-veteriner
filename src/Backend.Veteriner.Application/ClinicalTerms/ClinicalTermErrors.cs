using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalTerms;

/// <summary>Sözlük yazma uçlarının ortak hata kodları (HTTP eşlemesi <c>ResultExtensions</c> içinde koddan türer).</summary>
internal static class ClinicalTermErrors
{
    public static readonly Error TenantContextMissing =
        new("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

    public static readonly Error NotFound = new("ClinicalTerms.NotFound", "Sözlük kaydı bulunamadı.");

    /// <summary>409: aynı tür ve adla (Türkçe harf/büyük-küçük farkı yok sayılır) kayıt zaten var (sistem, pasif dahil).</summary>
    public static readonly Error DuplicateName =
        new("ClinicalTerms.DuplicateName", "Bu ad sözlükte zaten var (pasif kayıtlar dahil).");

    /// <summary>409: kayıt siz düzenlerken değişti.</summary>
    public static readonly Error ConcurrencyConflict =
        new("ClinicalTerms.ConcurrencyConflict", "Kayıt başka biri tarafından değiştirildi; listeyi yenileyip tekrar deneyin.");

    public static readonly Error InvalidRowVersion =
        new("ClinicalTerms.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

    public static bool IsDuplicateNameViolation(DbUpdateException ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains("IX_ClinicalTerms_TenantId_Kind_NormalizedName", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
