using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Administrations.Commands.Create;

/// <summary>
/// POST /api/v1/administrations. Zorunlu: <see cref="PetId"/>, <see cref="ProductId"/>, <see cref="Quantity"/>.
/// Opsiyonel: <see cref="ClinicId"/> (klinik bağlamı yoksa zorunlu), <see cref="ExaminationId"/>, <see cref="TreatmentId"/>,
/// <see cref="AdministeredAtUtc"/>, <see cref="RequestId"/> (tekrar koruması). Stok düşümü aynı transaction'da yazılır.
/// <see cref="BatchId"/> düşülecek partidir; verilmezse FEFO ile süresi geçmemiş ilk yeterli parti seçilir.
///
/// <see cref="VaccinationId"/> yalnız aşı kaydı oluşturma akışı tarafından verilir (HTTP gövdesinde yoktur); uygulamayı o aşı kaydına bağlar.
/// <see cref="PackageChargeSuggestionId"/> yalnız fiyatlı tedavi paketi uygulaması verir (HTTP gövdesinde yoktur): uygulama pakete dahildir, kendi kalemini doğurmaz.
///
/// <see cref="LateEntryReason"/> bağlı muayene kesinleşmişse zorunludur (sonradan ekleme), aksi hâlde yok sayılır.
///
/// <see cref="Route"/> ve <see cref="Site"/> (RECORD-005) isteğe bağlıdır; aşıya bağlı uygulamada verilmez (aşı kaydında tutulur).
/// </summary>
public sealed record CreateAdministrationCommand(
    Guid? ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    Guid? TreatmentId,
    Guid ProductId,
    decimal Quantity,
    DateTime? AdministeredAtUtc,
    string? Notes,
    string? LateEntryReason,
    Guid? RequestId,
    Guid? BatchId = null,
    Guid? VaccinationId = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AdministrationRoute? Route = null,
    string? Site = null,
    Guid? PackageChargeSuggestionId = null)
    : IRequest<Result<AdministrationCreateResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "Administration.Create";
    public string? AuditTarget => $"PetId={PetId};ProductId={ProductId}";
}
