using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Treatments.Commands.Create;

public sealed record CreateTreatmentCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime TreatmentDateUtc,
    string Title,
    string Description,
    string? Notes,
    DateTime? FollowUpDateUtc)
    : IRequest<Result<Guid>>;
