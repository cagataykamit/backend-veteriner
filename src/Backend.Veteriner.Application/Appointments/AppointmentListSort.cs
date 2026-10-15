using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Models;

namespace Backend.Veteriner.Application.Appointments;

internal enum AppointmentListSortField
{
    ScheduledAtUtc
}

/// <summary>
/// Randevu listesi sıralaması: yalnızca <c>scheduledAtUtc</c> (büyük/küçük harf duyarsız).
/// Sort boşsa varsayılan: en yeni randevu üstte (desc); bu durumda <see cref="PageRequest.Order"/> yok sayılır.
/// </summary>
internal static class AppointmentListSort
{
    public static readonly ListSortCatalog<AppointmentListSortField> Catalog = new(
        ("scheduledAtUtc", AppointmentListSortField.ScheduledAtUtc));

    /// <summary>
    /// <see cref="Appointment.ScheduledAtUtc"/> için azalan mı (en yeni önce).
    /// </summary>
    public static bool ResolveScheduledAtDescending(PageRequest page)
        => Catalog.Resolve(page)?.Descending ?? true;
}
