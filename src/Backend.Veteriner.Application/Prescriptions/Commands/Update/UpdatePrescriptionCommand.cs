using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Prescriptions.Commands.Update;

public sealed record UpdatePrescriptionCommand(
    Guid Id,
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
    : IRequest<Result>;
