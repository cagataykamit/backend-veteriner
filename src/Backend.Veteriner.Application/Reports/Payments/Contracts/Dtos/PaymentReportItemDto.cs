using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;

/// <summary>
/// <c>GET /reports/payments</c> JSON yanıtı (tam alan seti). CSV export bu DTO’dan üretilir ancak dosyada teknik ID kolonları yer almaz;
/// klinik içi tahsilat raporu biçimi (Türkçe başlık, Europe/Istanbul tarih, vb.) uygulanır.
/// </summary>
public sealed record PaymentReportItemDto(
    Guid PaymentId,
    DateTime PaidAtUtc,
    Guid ClinicId,
    string ClinicName,
    Guid ClientId,
    string ClientName,
    Guid? PetId,
    string PetName,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    string? Notes);
