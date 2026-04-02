using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Commands.Create;

/// <summary>
/// POST /api/v1/payments gövdesi (controller doğrudan bu komutu bağlar).
/// Zorunlu: <see cref="ClinicId"/>, <see cref="ClientId"/>, <see cref="Amount"/>, <see cref="Currency"/>, <see cref="Method"/>, <see cref="PaidAtUtc"/>.
/// Opsiyonel: <see cref="PetId"/>, <see cref="AppointmentId"/>, <see cref="ExaminationId"/>, <see cref="Notes"/>.
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
    string? Notes)
    : IRequest<Result<Guid>>;
