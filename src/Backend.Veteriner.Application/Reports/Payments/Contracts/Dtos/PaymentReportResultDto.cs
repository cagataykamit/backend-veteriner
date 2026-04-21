namespace Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;

public sealed record PaymentReportResultDto(
    int TotalCount,
    decimal TotalAmount,
    IReadOnlyList<PaymentReportItemDto> Items);
