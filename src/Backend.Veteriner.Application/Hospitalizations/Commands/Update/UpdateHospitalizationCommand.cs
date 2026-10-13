using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Update;

/// <summary>
/// Tam güncelleme. <see cref="VisitId"/> verilmezse muayene değişmediyse mevcut bağ korunur, değiştiyse yeni muayeneden miras alınır.
/// <see cref="Location"/>: null = dokunma, boş = temizle. <see cref="RowVersion"/> geçiş döneminde opsiyoneldir (verilirse denetlenir).
/// </summary>
public sealed record UpdateHospitalizationCommand(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime AdmittedAtUtc,
    DateTime? PlannedDischargeAtUtc,
    string Reason,
    string? Notes,
    Guid? VisitId = null,
    string? Location = null,
    string? RowVersion = null)
    : IRequest<Result<HospitalizationWriteResultDto>>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "Hospitalization.Update";
    public string? AuditTarget => $"HospitalizationId={Id}";
}
