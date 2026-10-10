using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Commands.Update;

/// <summary>
/// <see cref="RowVersion"/>: istemcinin formu açarken GET ile aldığı sürüm (Base64); zorunlu.
/// <see cref="AppointmentId"/> null ise mevcut randevu bağlantısı korunur.
/// Kısmi güncelleme: metin alanlarında null = dokunma, boş/boşluk = temizle; vitallerde null = dokunma, <see cref="ClearVitals"/> = temizle.
/// Kesinleşmiş muayene güncellenemez. Klinik metin alanları audit yüküne yazılmaz (<c>JsonIgnore</c>).
/// </summary>
public sealed record UpdateExaminationCommand(
    Guid Id,
    Guid? ClinicId,
    Guid? PetId,
    Guid? AppointmentId,
    DateTime ExaminedAtUtc,
    [property: JsonIgnore] string VisitReason,
    [property: JsonIgnore] string? Findings,
    [property: JsonIgnore] string? Assessment,
    [property: JsonIgnore] string? Notes,
    string? RowVersion = null,
    [property: JsonIgnore] string? Anamnesis = null,
    [property: JsonIgnore] string? Plan = null,
    decimal? WeightKg = null,
    decimal? TemperatureC = null,
    int? HeartRateBpm = null,
    int? RespiratoryRatePerMin = null,
    DateTime? VitalsMeasuredAtUtc = null,
    bool ClearVitals = false)
    : IRequest<Result<ExaminationWriteResultDto>>, IAuditableRequest
{
    public string AuditAction => "Examination.Update";
    public string? AuditTarget => $"ExaminationId={Id}";
}
