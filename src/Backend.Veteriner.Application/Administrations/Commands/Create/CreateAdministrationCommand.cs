using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Administrations.Commands.Create;

/// <summary>
/// POST /api/v1/administrations. Zorunlu: <see cref="PetId"/>, <see cref="ProductId"/>, <see cref="Quantity"/>.
/// Opsiyonel: <see cref="ClinicId"/> (klinik bağlamı yoksa zorunlu), <see cref="ExaminationId"/>, <see cref="TreatmentId"/>,
/// <see cref="AdministeredAtUtc"/>, <see cref="RequestId"/> (tekrar koruması). Stok düşümü aynı transaction'da yazılır.
/// <see cref="BatchId"/> düşülecek partidir; verilmezse FEFO ile süresi geçmemiş ilk yeterli parti seçilir.
///
/// <see cref="LateEntryReason"/> bağlı muayene kesinleşmişse zorunludur (sonradan ekleme), aksi hâlde yok sayılır.
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
    Guid? BatchId = null)
    : IRequest<Result<AdministrationCreateResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "Administration.Create";
    public string? AuditTarget => $"PetId={PetId};ProductId={ProductId}";
}
