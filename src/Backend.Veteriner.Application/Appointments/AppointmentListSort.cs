using Backend.Veteriner.Application.Common.Models;

namespace Backend.Veteriner.Application.Appointments;

/// <summary>
/// Randevu listesi sıralaması: yalnızca <c>scheduledAtUtc</c> (büyük/küçük harf duyarsız).
/// Sort boşsa varsayılan: en yeni randevu üstte (desc); bu durumda <see cref="PageRequest.Order"/> yok sayılır.
/// </summary>
internal static class AppointmentListSort
{
    public const string ScheduledAtUtcSortKey = "scheduledAtUtc";

    /// <summary>
    /// <see cref="Appointment.ScheduledAtUtc"/> için azalan mı (en yeni önce).
    /// </summary>
    public static bool ResolveScheduledAtDescending(PageRequest page)
    {
        var sort = page.Sort?.Trim();
        if (string.IsNullOrEmpty(sort))
            return true;

        if (!string.Equals(sort, ScheduledAtUtcSortKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sort alanı doğrulanmış olmalıdır.");

        var order = page.Order?.Trim();
        if (string.IsNullOrEmpty(order))
            return true;

        return string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
    }
}
