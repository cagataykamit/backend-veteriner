using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Payments.Commands.Create;

/// <summary>
/// POST /api/v1/payments gövdesi (controller doğrudan bu komutu bağlar).
/// Zorunlu: <see cref="ClinicId"/>, <see cref="ClientId"/>, <see cref="Amount"/>, <see cref="Currency"/>, <see cref="Method"/>, <see cref="PaidAtUtc"/>,
/// <see cref="RequestId"/> (çift istek koruması) ve <see cref="Allocations"/>; tahsis toplamı + <see cref="CreditAmount"/> = <see cref="Amount"/>.
/// <see cref="CreditAmount"/> > 0 ise fazlası (ya da tahsissiz tamamı, avans) müşteri kredisine yazılır (FIN-005).
/// Opsiyonel: <see cref="PetId"/>, <see cref="AppointmentId"/>, <see cref="ExaminationId"/>, <see cref="Notes"/>.
/// Ödeme, tahsisler ve sayaçlar tek veritabanı işleminde yazılır; hata durumunda hiçbiri kalmaz.
/// </summary>
public sealed record CreatePaymentCommand(
    Guid ClinicId,
    Guid ClientId,
    Guid? PetId,
    Guid? AppointmentId,
    Guid? ExaminationId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    DateTime PaidAtUtc,
    string? Notes,
    Guid RequestId = default,
    IReadOnlyList<PaymentAllocationLineDto>? Allocations = null,
    decimal CreditAmount = 0m)
    : IRequest<Result<PaymentCreateResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest, IActivationSignalRequest
{
    public ActivationSignalKind ActivationSignal => ActivationSignalKind.PaymentCreated;
    public string AuditAction => "Payment.Create";
    public string? AuditTarget => $"ClientId={ClientId}";
}
