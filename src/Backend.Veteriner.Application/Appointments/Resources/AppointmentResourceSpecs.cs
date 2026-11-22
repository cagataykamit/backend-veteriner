using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Resources;

public sealed class AppointmentResourceByIdSpec : Specification<AppointmentResource>
{
    public AppointmentResourceByIdSpec(Guid tenantId, Guid clinicId, Guid id)
        => Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.Id == id);
}

/// <summary>Kliniğin kaynakları: önce odalar, sonra cihazlar; tür içinde <c>SortOrder</c> sonra ad (takvim sütun sırası).</summary>
public sealed class AppointmentResourcesByClinicSpec : Specification<AppointmentResource>
{
    public AppointmentResourcesByClinicSpec(Guid tenantId, Guid clinicId)
    {
        Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId);
        Query.OrderBy(x => x.Type).ThenBy(x => x.SortOrder).ThenBy(x => x.Name);
    }
}

/// <summary>Kimliklere göre kaynaklar (takvimde ad; pasif kaynak da adıyla görünür).</summary>
public sealed class AppointmentResourcesByIdsSpec : Specification<AppointmentResource>
{
    public AppointmentResourcesByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var idArray = ids.ToArray();
        Query.AsNoTracking().Where(x => x.TenantId == tenantId && idArray.Contains(x.Id));
    }
}

/// <summary>Aktif kaynaklar arasında ad eşleşmesi (büyük/küçük harf duyarsız). Veritabanı karşılığı: filtreli unique indeks.</summary>
public sealed class ActiveAppointmentResourceByClinicAndNameSpec : Specification<AppointmentResource>
{
    public ActiveAppointmentResourceByClinicAndNameSpec(
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

/// <summary>
/// Kaynağı kullanan, aralıkla kesişen planlı randevular (ADR-053 K4): oda ya da cihaz olarak. Kapasite hesabı için yalnız zamanları okunur.
/// </summary>
public sealed class AppointmentIntervalsUsingResourceSpec : Specification<Appointment, AppointmentInterval>
{
    public AppointmentIntervalsUsingResourceSpec(
        Guid tenantId,
        Guid resourceId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId)
    {
        Query.AsNoTracking().Where(a =>
            a.TenantId == tenantId
            && (a.RoomResourceId == resourceId || a.EquipmentResourceId == resourceId)
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc < endUtc
            && startUtc < a.ScheduledAtUtc.AddMinutes(a.DurationMinutes));

        if (excludeAppointmentId is { } excludeId)
            Query.Where(a => a.Id != excludeId);

        Query.Select(a => new AppointmentInterval(a.ScheduledAtUtc, a.DurationMinutes));
    }
}

public sealed record AppointmentInterval(DateTime StartUtc, int DurationMinutes)
{
    public DateTime EndUtc => StartUtc.AddMinutes(DurationMinutes);
}
