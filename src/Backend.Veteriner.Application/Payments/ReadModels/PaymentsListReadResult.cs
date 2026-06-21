using Backend.Veteriner.Application.Payments.Contracts.Dtos;

namespace Backend.Veteriner.Application.Payments.ReadModels;

public sealed record PaymentsListReadResult(
    IReadOnlyList<PaymentListItemDto> Items,
    int TotalCount);
