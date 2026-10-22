using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;

namespace Backend.Veteriner.Application.Visits.ReadModels;

/// <summary>Açık iş hesabında kullanıcının görebildiği kayıt türleri (okuma izni olmayan tür sayılmaz).</summary>
[Flags]
public enum OpenItemSources
{
    None = 0,
    Examinations = 1,
    Treatments = 2,
    Administrations = 4,
    Vaccinations = 8,
    Hospitalizations = 16,
    LabResults = 32,
    Charges = 64,
    Prescriptions = 128
}

public sealed record OpenItemsScope(Guid TenantId, Guid ClinicId, OpenItemSources Visible)
{
    public bool Sees(OpenItemSources source) => (Visible & source) == source;

    /// <summary>Kullanıcının izinlerinden görünür türleri çıkarır (tek yer).</summary>
    public static OpenItemsScope For(Guid tenantId, Guid clinicId, ICurrentUserPermissionChecker permissions)
    {
        var visible = OpenItemSources.None;
        Add(OpenItemSources.Examinations, PermissionCatalog.Examinations.Read);
        Add(OpenItemSources.Treatments, PermissionCatalog.Treatments.Read);
        Add(OpenItemSources.Administrations, PermissionCatalog.Administrations.Read);
        Add(OpenItemSources.Vaccinations, PermissionCatalog.Vaccinations.Read);
        Add(OpenItemSources.Hospitalizations, PermissionCatalog.Hospitalizations.Read);
        Add(OpenItemSources.LabResults, PermissionCatalog.LabResults.Read);
        Add(OpenItemSources.Charges, PermissionCatalog.Charges.Read);
        Add(OpenItemSources.Prescriptions, PermissionCatalog.Prescriptions.Read);
        return new OpenItemsScope(tenantId, clinicId, visible);

        void Add(OpenItemSources source, string permission)
        {
            if (permissions.HasPermission(permission))
                visible |= source;
        }
    }
}

/// <summary>
/// Ziyaret başına açık iş alanları. İzin yoksa ilgili alanlar null'dır (<see cref="ChargeIndicator"/> ve tutarlar için
/// <c>Charges.Read</c>, <see cref="AwaitingLabCount"/> için <c>LabResults.Read</c>).
/// </summary>
public sealed record VisitOpenItems(
    VisitChargeIndicator? ChargeIndicator,
    int? ChargeableWorkCount,
    decimal? ChargedAmount,
    decimal? AllocatedAmount,
    int? AwaitingLabCount);

/// <param name="UnchargedFromUtc">Ücretsiz ziyaret sayımı penceresi başlangıcı (dahil).</param>
/// <param name="UnchargedToUtc">Ücretsiz ziyaret sayımı penceresi sonu (hariç).</param>
/// <param name="IncompleteBeforeUtc">Bu andan önce gelip tamamlanmamış geliş sayılır (içinde bulunulan günün başlangıcı).</param>
/// <param name="HbsFromUtc">"HBS'e girilmedi" sayım penceresi başlangıcı (dahil); null ise HBS sayıları hesaplanmaz (gün sonu).</param>
/// <param name="HbsToUtc">"HBS'e girilmedi" sayım penceresi sonu (hariç).</param>
public sealed record OpenItemsSummaryRequest(
    OpenItemsScope Scope,
    DateTime UnchargedFromUtc,
    DateTime UnchargedToUtc,
    DateTime IncompleteBeforeUtc,
    DateTime? HbsFromUtc = null,
    DateTime? HbsToUtc = null);

/// <summary>
/// Açık iş göstergelerinin (ücret yok, ödeme bekliyor, lab bekliyor, tamamlanmamış geliş) tek hesap yeri.
/// Bugün ve gün sonu raporu aynı kuralı buradan alır. Yalnızca verilen kiracı ve klinikte okur.
/// </summary>
public interface IVisitOpenItemsReader
{
    /// <summary>Verilen ziyaretler için alanlar (sabit sayıda sorgu; ziyaret başına sorgu yok). Ücreti/işlemi olmayan ziyaret de anahtar olarak döner.</summary>
    Task<IReadOnlyDictionary<Guid, VisitOpenItems>> GetForVisitsAsync(
        OpenItemsScope scope, IReadOnlyCollection<Guid> visitIds, CancellationToken ct = default);

    Task<OpenItemsSummaryDto> GetSummaryAsync(OpenItemsSummaryRequest request, CancellationToken ct = default);
}
