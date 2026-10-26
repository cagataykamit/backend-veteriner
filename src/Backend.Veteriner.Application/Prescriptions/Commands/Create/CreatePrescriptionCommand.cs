using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

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
    DateTime? FollowUpDateUtc,
    IReadOnlyList<PrescriptionItemRequest>? Items = null)
    : IRequest<Result<Guid>>;
