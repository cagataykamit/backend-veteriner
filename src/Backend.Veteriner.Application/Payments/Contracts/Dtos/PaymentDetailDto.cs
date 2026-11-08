using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Contracts.Dtos;

/// <summary>GET /payments/{id} yanıtı. <see cref="PetId"/>, <see cref="AppointmentId"/>, <see cref="ExaminationId"/>, <see cref="Notes"/> null olabilir. <see cref="CashRegisterName"/> yalnız nakitte dolu (kasası boşsa Ana kasa); Query DB yolunda null.</summary>
public sealed record PaymentDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    Guid? PetId,
    string PetName,
    Guid? AppointmentId,
    Guid? ExaminationId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    DateTime PaidAtUtc,
    string? Notes,
    decimal? AllocatedAmount = null,
    decimal? UnallocatedAmount = null,
    int? PetCount = null,
    decimal? CreditedAmount = null,
    Guid? CashRegisterId = null,
    string? CashRegisterName = null);
