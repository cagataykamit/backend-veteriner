using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

public sealed record PaymentListRow(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    Guid? PetId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    DateTime PaidAtUtc);
