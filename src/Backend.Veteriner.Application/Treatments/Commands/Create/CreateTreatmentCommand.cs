using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Treatments.Commands.Create;

/// <summary>
/// POST /api/v1/treatments. <see cref="LateEntryReason"/> bağlı muayene kesinleşmişse zorunludur (5–500; ADR-043 AK-6, ADR-013 Ek 2):
/// tedavi "Kesinleştirmeden sonra eklendi" olarak yazılır.
/// </summary>
public sealed record CreateTreatmentCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime TreatmentDateUtc,
    string Title,
    string Description,
    string? Notes,
    DateTime? FollowUpDateUtc,
    string? LateEntryReason = null)
    : IRequest<Result<Guid>>;
