using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;

namespace Backend.Veteriner.Application.Reports.Payments.ReadModels;

/// <summary>
/// Query DB payment report JSON okuma sonucu: SQL tarafında hesaplanan aggregate'ler (<see cref="TotalCount"/> /
/// <see cref="TotalAmount"/>) + sayfalanmış <see cref="Items"/>. <see cref="PaymentReportResultDto"/> ile birebir
/// uyumludur; handler sonucu doğrudan DTO'ya kopyalar.
/// </summary>
public sealed record PaymentsReportReadResult(
    int TotalCount,
    decimal TotalAmount,
    IReadOnlyList<PaymentReportItemDto> Items);
