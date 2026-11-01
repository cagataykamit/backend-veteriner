using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalSnippets;

/// <summary>Metin parçası yazma uçlarının ortak hata kodları (HTTP eşlemesi <c>ResultExtensions</c> içinde koddan türer).</summary>
internal static class ClinicalSnippetErrors
{
    public const string ShortcutIndexName = "IX_ClinicalSnippets_TenantId_Shortcut";

    public static readonly Error TenantContextMissing =
        new("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

    public static readonly Error NotFound = new("ClinicalSnippets.NotFound", "Metin parçası bulunamadı.");

    /// <summary>409: aynı kısayol (Türkçe harf/büyük-küçük farkı yok sayılır) kiracıda zaten var (pasif dahil).</summary>
    public static readonly Error DuplicateShortcut =
        new("ClinicalSnippets.DuplicateShortcut", "Bu kısayol zaten kullanılıyor (pasif parçalar dahil).");

    /// <summary>409: kiracı başına tavan doldu.</summary>
    public static readonly Error LimitReached =
        new("ClinicalSnippets.LimitReached", $"Kiracı başına en fazla {Domain.Catalog.ClinicalSnippet.MaxPerTenant} metin parçası tanımlanabilir; kullanılmayanları pasifleştirmek yerine düzenleyin.");

    /// <summary>409: kayıt siz düzenlerken değişti.</summary>
    public static readonly Error ConcurrencyConflict =
        new("ClinicalSnippets.ConcurrencyConflict", "Kayıt başka biri tarafından değiştirildi; listeyi yenileyip tekrar deneyin.");

    public static readonly Error InvalidRowVersion =
        new("ClinicalSnippets.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

    public static bool IsDuplicateShortcutViolation(DbUpdateException ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains(ShortcutIndexName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
