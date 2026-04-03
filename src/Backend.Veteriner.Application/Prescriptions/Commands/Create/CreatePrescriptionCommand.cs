using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Prescriptions.Commands.Create;

public sealed record CreatePrescriptionCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    Guid? TreatmentId,
    DateTime PrescribedAtUtc,
    string Title,
    string Content,
    string? Notes,
    DateTime? FollowUpDateUtc)
    : IRequest<Result<Guid>>;
