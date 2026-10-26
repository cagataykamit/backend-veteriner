using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Sales.Commands.Create;

/// <summary>
/// POST /api/v1/sales. Zorunlu: <see cref="RequestId"/> ve en az bir <see cref="Lines"/> satırı.
/// Opsiyonel: <see cref="ClinicId"/> (klinik bağlamı yoksa zorunlu), <see cref="ClientId"/> (yoksa Peşin müşteri), <see cref="Notes"/>,
/// <see cref="Payment"/> (Peşin müşteride zorunlu ve tam tutar; müşterilide yoksa veresiye).
/// Stok düşümü, tek ücret ve ödeme aynı transaction'da yazılır; biri başarısız olursa hiçbiri kalmaz.
/// </summary>
public sealed record CreateSaleCommand(
    Guid? ClinicId,
    Guid? ClientId,
    Guid RequestId,
    string? Notes,
    IReadOnlyList<CreateSaleLineDto>? Lines,
    CreateSalePaymentDto? Payment)
    : IRequest<Result<SaleCreateResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest, IActivationSignalRequest
{
    public ActivationSignalKind ActivationSignal => ActivationSignalKind.SaleCreated;
    public string AuditAction => "Sale.Create";
    public string? AuditTarget => $"ClientId={ClientId?.ToString() ?? "walk-in"}";
}
