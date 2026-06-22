using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;

namespace Backend.Veteriner.Application.Reports.Payments.ReadModels;

/// <summary>
/// Query DB payment export okuma sonucu: SQL <c>COUNT(*)</c> + tüm eşleşen <see cref="Items"/>.
/// Limit aşımında yalnızca <see cref="TotalCount"/> döner; items boş kalır.
/// </summary>
public sealed record PaymentsReportExportReadResult(
    int TotalCount,
    IReadOnlyList<PaymentReportItemDto> Items);
