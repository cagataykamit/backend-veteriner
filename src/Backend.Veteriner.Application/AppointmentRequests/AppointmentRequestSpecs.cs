using Ardalis.Specification;
using Backend.Veteriner.Domain.AppointmentRequests;

namespace Backend.Veteriner.Application.AppointmentRequests;

public sealed class AppointmentRequestByIdSpec : Specification<AppointmentRequest>
{
    public AppointmentRequestByIdSpec(Guid tenantId, Guid id)
        => Query.Where(x => x.TenantId == tenantId && x.Id == id);
}

/// <summary>Aynı e-posta + klinik için bekleyen talepler (ADR-042 K6 sınırı).</summary>
public sealed class PendingAppointmentRequestsByEmailSpec : Specification<AppointmentRequest>
{
    public PendingAppointmentRequestsByEmailSpec(Guid tenantId, Guid clinicId, string normalizedEmail)
        => Query.Where(x =>
            x.TenantId == tenantId
            && x.ClinicId == clinicId
            && x.ContactEmail == normalizedEmail
            && x.Status == AppointmentRequestStatus.Pending);
}

/// <summary>Portal "taleplerim": bağın müşterisinin bu klinikteki talepleri, yeniden eskiye.</summary>
public sealed class PortalAppointmentRequestsSpec : Specification<AppointmentRequest>
{
    public PortalAppointmentRequestsSpec(Guid tenantId, Guid clinicId, Guid clientId, int take)
        => Query.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.ClientId == clientId && x.Source == AppointmentRequestSource.Portal)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take);
}

/// <summary>
/// Personel kuyruğu: kiracı, isteğe bağlı klinik kümesi, durum ve istenen saat aralığı. Bekleyenler en yakın saat önce; diğerleri
/// en yeni önce.
/// </summary>
public sealed class StaffAppointmentRequestsSpec : Specification<AppointmentRequest>
{
    public StaffAppointmentRequestsSpec(
        Guid tenantId,
        IReadOnlyCollection<Guid>? clinicIds,
        AppointmentRequestStatus? status,
        DateTime? fromUtc,
        DateTime? toUtc,
        int? page = null,
        int? pageSize = null)
    {
        Query.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (clinicIds is not null)
        {
            var ids = clinicIds.ToArray();
            Query.Where(x => ids.Contains(x.ClinicId));
        }

        if (status is { } s)
            Query.Where(x => x.Status == s);
        if (fromUtc is { } from)
            Query.Where(x => x.RequestedStartUtc >= from);
        if (toUtc is { } to)
            Query.Where(x => x.RequestedStartUtc < to);

        if (status == AppointmentRequestStatus.Pending)
            Query.OrderBy(x => x.RequestedStartUtc).ThenBy(x => x.CreatedAtUtc);
        else
            Query.OrderByDescending(x => x.CreatedAtUtc);

        if (page is { } p && pageSize is { } size)
            Query.Skip((p - 1) * size).Take(size);
    }
}

/// <summary>Süresi dolan bekleyen talepler (arka plan işi; sistem kapsamında kiracılar arası).</summary>
public sealed class DueAppointmentRequestsSpec : Specification<AppointmentRequest>
{
    public DueAppointmentRequestsSpec(DateTime utcNow, int take)
        => Query.Where(x => x.Status == AppointmentRequestStatus.Pending && x.RequestedStartUtc <= utcNow)
            .OrderBy(x => x.RequestedStartUtc)
            .Take(take);
}

public sealed class OpenAppointmentRequestVerificationsSpec : Specification<AppointmentRequestEmailVerification>
{
    public OpenAppointmentRequestVerificationsSpec(Guid tenantId, Guid clinicId, string normalizedEmail, DateTime utcNow)
        => Query.Where(x =>
            x.TenantId == tenantId
            && x.ClinicId == clinicId
            && x.Email == normalizedEmail
            && x.ConsumedAtUtc == null
            && x.InvalidatedAtUtc == null
            && x.ExpiresAtUtc > utcNow);
}

public sealed class LatestAppointmentRequestVerificationSpec : Specification<AppointmentRequestEmailVerification>
{
    public LatestAppointmentRequestVerificationSpec(Guid tenantId, Guid clinicId, string normalizedEmail)
        => Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.Email == normalizedEmail)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(1);
}

public sealed class ClinicAppointmentRequestEmailTemplatesSpec : Specification<ClinicAppointmentRequestEmailTemplate>
{
    public ClinicAppointmentRequestEmailTemplatesSpec(Guid tenantId, Guid clinicId, AppointmentRequestEmailKind? kind = null)
    {
        Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId);
        if (kind is { } k)
            Query.Where(x => x.Kind == k);
    }
}
