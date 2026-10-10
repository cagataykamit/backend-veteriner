using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.Contracts;

public static class VisitMappings
{
    /// <summary>Sorumlu hekimin görünen adı <paramref name="names"/> ile çözülür (hekim yoksa null).</summary>
    public static async Task<VisitDto> ToDtoAsync(this Visit v, IClinicVeterinarianReader names, CancellationToken ct)
    {
        string? responsibleName = null;
        if (v.ResponsibleVeterinarianUserId is { } responsibleId)
            (await names.GetNamesAsync([responsibleId], ct)).TryGetValue(responsibleId, out responsibleName);

        return new VisitDto(
            v.Id,
            v.ClinicId,
            v.PetId,
            v.AppointmentId,
            v.ResponsibleVeterinarianUserId,
            responsibleName,
            v.ArrivedAtUtc,
            v.CareStatus,
            v.StartedAtUtc,
            v.CompletedAtUtc,
            v.IsVoided,
            v.VoidReason,
            v.CreatedByUserId,
            v.CreatedAtUtc);
    }
}
