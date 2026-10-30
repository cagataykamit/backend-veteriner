using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Reasons;

/// <summary>Randevu nedeni; <see cref="VeterinarianUserIds"/> boşsa klinikteki tüm hekimler uygundur.</summary>
public sealed record AppointmentReasonDto(
    Guid Id,
    Guid ClinicId,
    string Name,
    int DefaultDurationMinutes,
    bool IsOnlineBookable,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<Guid> VeterinarianUserIds);

public static class AppointmentReasonMapping
{
    public static AppointmentReasonDto ToDto(AppointmentReason r) => new(
        r.Id,
        r.ClinicId,
        r.Name,
        r.DefaultDurationMinutes,
        r.IsOnlineBookable,
        r.SortOrder,
        r.IsActive,
        r.Veterinarians.Select(v => v.UserId).ToList());

    /// <summary>Ad benzersizlik araması için: <c>Trim().ToLowerInvariant()</c>.</summary>
    public static string NormalizeNameForLookup(string name) => (name ?? string.Empty).Trim().ToLowerInvariant();
}
