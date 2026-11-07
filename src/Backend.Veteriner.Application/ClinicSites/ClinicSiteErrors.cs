using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicSites;

/// <summary>Site uçlarının sabit hataları (docs/CLINIC_SITE_API_CONTRACT.md §7).</summary>
public static class ClinicSiteErrors
{
    /// <summary>Adres benzersizliği: <c>IX_ClinicSiteAddresses_Slug</c> ve <c>IX_ClinicSites_Slug</c>; yarışta ikisinden biri yakalar.</summary>
    private static readonly string[] SlugIndexNames = ["IX_ClinicSiteAddresses_Slug", "IX_ClinicSites_Slug"];

    /// <summary>Çift tıklamada iki yayın aynı sürüm numarasını yazar; hangisi önce çalışırsa diğeri bu indekse takılır.</summary>
    private const string VersionNumberIndexName = "IX_ClinicSiteVersions_TenantId_SiteId_VersionNumber";

    public static readonly Error NotFound =
        new("ClinicSites.NotFound", "Bu klinik için site oluşturulmamış.");

    public static readonly Error VersionNotFound =
        new("ClinicSites.VersionNotFound", "Sürüm bulunamadı.");

    public static readonly Error ImageNotFound =
        new("ClinicSites.ImageNotFound", "Görsel bulunamadı.");

    public static readonly Error PublicNotFound =
        new("PublicSites.NotFound", "Site bulunamadı.");

    public static readonly Error SlugTaken =
        new("ClinicSites.SlugTaken", "Bu adres kullanılamıyor; başka bir adres seçin.");

    public static readonly Error ConcurrencyConflict =
        new("ClinicSites.ConcurrencyConflict", "Site başka bir oturumda değiştirildi; sayfayı yenileyip tekrar deneyin.");

    public static readonly Error ClinicInactive =
        new("ClinicSites.ClinicInactive", "Pasif klinik için site yayınlanamaz.");

    public static readonly Error InvalidRowVersion =
        new(ClinicSiteConfig.ValidationCode, "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

    public static Error InvalidSlug(ClinicSiteSlugCheck check) => check == ClinicSiteSlugCheck.Reserved
        ? new(ClinicSiteConfig.ValidationCode, "Bu adres ayrılmış bir sözcük; başka bir adres seçin.")
        : new(
            ClinicSiteConfig.ValidationCode,
            $"Adres {ClinicSiteSlug.MinLength}-{ClinicSiteSlug.MaxLength} karakter olmalı; yalnız küçük harf, rakam ve tire içerebilir.");

    public static Error PublishRequirementsNotMet(IReadOnlyList<string> missing, IReadOnlyList<Guid> missingImageIds)
    {
        var parts = missing.Select(ClinicSitePublishRequirements.Label).ToList();
        if (missingImageIds.Count > 0)
            parts.Add("silinmiş görsellerin değiştirilmesi");

        return new Error(
            "ClinicSites.PublishRequirementsNotMet",
            $"Yayınlamadan önce tamamlayın: {string.Join(", ", parts)}.",
            new Dictionary<string, object?>
            {
                ["missing"] = missing,
                ["missingImageIds"] = missingImageIds,
            });
    }

    public static Error ImageInUse(IReadOnlyList<string> usedIn) => new(
        "ClinicSites.ImageInUse",
        "Görsel taslakta ya da yayındaki sürümde kullanılıyor; önce oradan kaldırın.",
        new Dictionary<string, object?> { ["usedIn"] = usedIn });

    public static bool IsSlugViolation(DbUpdateException ex) => MentionsAny(ex, SlugIndexNames);

    public static bool IsVersionNumberViolation(DbUpdateException ex) => MentionsAny(ex, VersionNumberIndexName);

    private static bool MentionsAny(DbUpdateException ex, params string[] indexNames)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (indexNames.Any(name => e.Message.Contains(name, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }
}
