using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.Contracts;

public static class VisitMappings
{
    public static VisitDto ToDto(this Visit v)
        => new(
            v.Id,
            v.ClinicId,
            v.PetId,
            v.AppointmentId,
            v.ResponsibleVeterinarianUserId,
            v.ArrivedAtUtc,
            v.CareStatus,
            v.StartedAtUtc,
            v.CompletedAtUtc,
            v.IsVoided,
            v.VoidReason,
            v.CreatedByUserId,
            v.CreatedAtUtc);
}
