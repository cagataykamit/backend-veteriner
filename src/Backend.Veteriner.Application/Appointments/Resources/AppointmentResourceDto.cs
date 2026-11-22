using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Resources;

/// <summary>Randevu kaynağı (ADR-053): oda ya da cihaz; <see cref="Capacity"/> aynı anda kaç randevu kullanabilir.</summary>
public sealed record AppointmentResourceDto(
    Guid Id,
    Guid ClinicId,
    string Name,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentResourceType Type,
    int Capacity,
    int SortOrder,
    bool IsActive);

public static class AppointmentResourceMapping
{
    public static AppointmentResourceDto ToDto(AppointmentResource r)
        => new(r.Id, r.ClinicId, r.Name, r.Type, r.Capacity, r.SortOrder, r.IsActive);

    /// <summary>Ad benzersizlik araması için: <c>Trim().ToLowerInvariant()</c>.</summary>
    public static string NormalizeNameForLookup(string name) => (name ?? string.Empty).Trim().ToLowerInvariant();
}
