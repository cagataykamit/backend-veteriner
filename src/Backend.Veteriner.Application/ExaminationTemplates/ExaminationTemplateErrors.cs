using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ExaminationTemplates;

/// <summary>Şablon uçlarının ortak hata kodları (HTTP eşlemesi <c>ResultExtensions</c> içinde koddan türer).</summary>
internal static class ExaminationTemplateErrors
{
    public static readonly Error TenantContextMissing =
        new("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

    public static readonly Error NotFound = new("ExaminationTemplates.NotFound", "Muayene şablonu bulunamadı.");

    /// <summary>409: aynı adla (Türkçe harf/büyük-küçük farkı yok sayılır) şablon zaten var (sistem, pasif dahil).</summary>
    public static readonly Error DuplicateName =
        new("ExaminationTemplates.DuplicateName", "Bu adla bir şablon zaten var (pasif şablonlar dahil).");

    /// <summary>409: şablon siz düzenlerken değişti.</summary>
    public static readonly Error ConcurrencyConflict =
        new("ExaminationTemplates.ConcurrencyConflict", "Şablon başka biri tarafından değiştirildi; yenileyip tekrar deneyin.");

    public static readonly Error InvalidRowVersion =
        new("ExaminationTemplates.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

    public static readonly Error Inactive =
        new("ExaminationTemplates.Inactive", "Pasif şablon uygulanamaz.");

    public static bool IsDuplicateNameViolation(DbUpdateException ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains("IX_ExaminationTemplates_TenantId_NormalizedName", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
