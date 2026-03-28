using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Contracts.Dtos;

public sealed record PaymentListItemDto(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    Guid? PetId,
    string PetName,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    DateTime PaidAtUtc);
