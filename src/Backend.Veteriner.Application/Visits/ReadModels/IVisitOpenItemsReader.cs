using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Visits;

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
    Prescriptions = 128,
    Appointments = 256
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
        Add(OpenItemSources.Appointments, PermissionCatalog.Appointments.Read);
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
/// <param name="OverdueAppointmentsFromUtc">Gecikmiş randevu sayım penceresi başlangıcı (dahil); null ise sayılmaz (gün sonu).</param>
/// <param name="OverdueAppointmentsBeforeUtc">Bu andan önceki planlı randevu gecikmiştir (içinde bulunulan günün başlangıcı).</param>
public sealed record OpenItemsSummaryRequest(
    OpenItemsScope Scope,
    DateTime UnchargedFromUtc,
    DateTime UnchargedToUtc,
    DateTime IncompleteBeforeUtc,
    DateTime? HbsFromUtc = null,
    DateTime? HbsToUtc = null,
    DateTime? OverdueAppointmentsFromUtc = null,
    DateTime? OverdueAppointmentsBeforeUtc = null)
{
    /// <summary>
    /// Bugün pencereleri (tek yer): ücretsiz ziyaret, "HBS'e girilmedi" ve gecikmiş randevu son 30 İstanbul günü; tamamlanmamış geliş ve
    /// gecikmiş randevu bugünden öncesi. Bugün özeti ve açık iş listesi aynı pencereyi buradan alır; sayı ile liste hep tutar.
    /// </summary>
    public static OpenItemsSummaryRequest ForToday(OpenItemsScope scope, DateTime nowUtc)
    {
        var todayLocal = OperationDayBounds.ToLocalDate(nowUtc);
        var todayStartUtc = OperationDayBounds.ForLocalDate(todayLocal).DayStartUtc;
        return new OpenItemsSummaryRequest(
            scope,
            OperationDayBounds.ForLocalDate(todayLocal.AddDays(-OpenItemsDefaults.UnchargedLookbackDays)).DayStartUtc,
            nowUtc,
            todayStartUtc,
            OperationDayBounds.ForLocalDate(todayLocal.AddDays(-OpenItemsDefaults.HbsLookbackDays)).DayStartUtc,
            nowUtc,
            OperationDayBounds.ForLocalDate(todayLocal.AddDays(-OpenItemsDefaults.OverdueAppointmentLookbackDays)).DayStartUtc,
            todayStartUtc);
    }
}

/// <summary>Açık iş listesindeki ziyaret satırı (ham; hekim adı ve işlem sayısı işleyicide eklenir).</summary>
/// <param name="LatestExaminationId">Ziyaretin en son muayenesi (yoksa null); ücret bu muayeneye bağlanabilir.</param>
public sealed record OpenItemVisitRow(
    Guid VisitId,
    DateTime ArrivedAtUtc,
    VisitCareStatus CareStatus,
    Guid PetId,
    string PetName,
    string? SpeciesName,
    Guid ClientId,
    string ClientName,
    Guid? ResponsibleVeterinarianUserId,
    Guid? LatestExaminationId);

/// <param name="TotalCount">Özetteki sayıyla aynı kural ve pencere; <paramref name="Rows"/> sınırla kesilmiş olabilir.</param>
public sealed record OpenItemVisitsReadResult(int TotalCount, IReadOnlyList<OpenItemVisitRow> Rows);

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

    /// <summary>
    /// Özetteki ziyaret sayılarının (<see cref="OpenItemVisitKind"/>) arkasındaki ziyaretler: özetle aynı sorgu parçası ve pencere,
    /// geliş zamanı artan (en eski önce), en çok <paramref name="maxItems"/> satır. Ücret yok için <c>Charges</c> görünürlüğü çağıranda denetlenir.
    /// </summary>
    Task<OpenItemVisitsReadResult> ListVisitsAsync(
        OpenItemsSummaryRequest request, OpenItemVisitKind kind, int maxItems, CancellationToken ct = default);
}
