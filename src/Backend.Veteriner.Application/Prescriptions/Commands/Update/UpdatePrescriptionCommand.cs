using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Prescriptions.Commands.Update;

/// <summary>PUT /api/v1/prescriptions/{id}. <see cref="RowVersion"/> zorunludur (ADR-043 K5).</summary>
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
    IReadOnlyList<PrescriptionItemRequest>? Items = null,
    string? RowVersion = null)
    : IRequest<Result>;
