using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Create;

/// <summary>
/// <see cref="VisitId"/> opsiyoneldir; verilmezse bağlı muayenenin gelişi miras alınır. <see cref="Location"/> serbest metindir.
/// Kesinleşmiş muayeneye bağlanamaz (<c>Examinations.Finalized</c>).
/// </summary>
public sealed record CreateHospitalizationCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime AdmittedAtUtc,
    DateTime? PlannedDischargeAtUtc,
    string Reason,
    string? Notes,
    Guid? VisitId = null,
    string? Location = null)
    : IRequest<Result<Guid>>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "Hospitalization.Create";
    public string? AuditTarget => $"PetId={PetId}";
}
