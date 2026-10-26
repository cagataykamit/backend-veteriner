using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using System.Text.Json.Serialization;

namespace Backend.Veteriner.Application.Examinations.Commands.Create;

/// <summary>
/// Klinik metin alanları audit yüküne yazılmaz (<c>JsonIgnore</c>).
/// Muayene, geliş durumu ve bağlı randevunun tamamlanması tek veritabanı işleminde yazılır; randevu tamamlanamazsa muayene de kalmaz.
/// </summary>
public sealed record CreateExaminationCommand(
    Guid? ClinicId,
    Guid? PetId,
    Guid? AppointmentId,
    DateTime ExaminedAtUtc,
    [property: JsonIgnore] string VisitReason,
    [property: JsonIgnore] string Findings,
    [property: JsonIgnore] string? Assessment,
    [property: JsonIgnore] string? Notes,
    [property: JsonIgnore] string? Anamnesis = null,
    [property: JsonIgnore] string? Plan = null,
    decimal? WeightKg = null,
    decimal? TemperatureC = null,
    int? HeartRateBpm = null,
    int? RespiratoryRatePerMin = null,
    DateTime? VitalsMeasuredAtUtc = null,
    Guid? VisitId = null)
    : IRequest<Result<ExaminationWriteResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest, IActivationSignalRequest
{
    public ActivationSignalKind ActivationSignal => ActivationSignalKind.ExaminationCreated;
    public string AuditAction => "Examination.Create";
    public string? AuditTarget => $"PetId={PetId}";
}
