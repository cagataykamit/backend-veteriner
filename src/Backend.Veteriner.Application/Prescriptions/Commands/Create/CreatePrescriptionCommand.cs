using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Prescriptions.Commands.Create;

/// <summary>
/// POST /api/v1/prescriptions. <see cref="LateEntryReason"/> bağlı muayene (doğrudan veya tedavi üzerinden) kesinleşmişse zorunludur
/// (sonradan ekleme, RECORD-008), aksi hâlde yok sayılır.
/// </summary>
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
    IReadOnlyList<PrescriptionItemRequest>? Items = null,
    string? LateEntryReason = null)
    : IRequest<Result<Guid>>;
