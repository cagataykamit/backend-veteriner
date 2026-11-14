using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Treatments.Commands.Update;

/// <summary>PUT /api/v1/treatments/{id}. <see cref="RowVersion"/> zorunludur (ADR-043 K5).</summary>
public sealed record UpdateTreatmentCommand(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime TreatmentDateUtc,
    string Title,
    string Description,
    string? Notes,
    DateTime? FollowUpDateUtc,
    string? RowVersion = null)
    : IRequest<Result>;
