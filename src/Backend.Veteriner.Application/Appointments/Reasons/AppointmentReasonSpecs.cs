using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Reasons;

public sealed class AppointmentReasonByIdSpec : Specification<AppointmentReason>
{
    public AppointmentReasonByIdSpec(Guid tenantId, Guid clinicId, Guid id)
        => Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.Id == id);
}

/// <summary>Kliniğin nedenleri; <c>SortOrder</c> sonra ad. <paramref name="onlineOnly"/>: yalnız aktif ve online'da görünür.</summary>
public sealed class AppointmentReasonsByClinicSpec : Specification<AppointmentReason>
{
    public AppointmentReasonsByClinicSpec(Guid tenantId, Guid clinicId, bool onlineOnly = false)
    {
        Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId);
        if (onlineOnly)
            Query.Where(x => x.IsActive && x.IsOnlineBookable);
        Query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name);
    }
}

/// <summary>Aktif nedenler arasında ad eşleşmesi (case-insensitive). Veritabanı karşılığı: filtreli unique indeks.</summary>
public sealed class ActiveAppointmentReasonByClinicAndNameSpec : Specification<AppointmentReason>
{
    public ActiveAppointmentReasonByClinicAndNameSpec(
        Guid tenantId,
        Guid clinicId,
        string normalizedNameLowerInvariant,
        Guid? excludeId = null)
    {
        Query.Where(x =>
            x.TenantId == tenantId
            && x.ClinicId == clinicId
            && x.IsActive
            && x.Name.ToLower() == normalizedNameLowerInvariant);
        if (excludeId is { } id)
            Query.Where(x => x.Id != id);
    }
}
