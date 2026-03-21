using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Commands.Create;

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
