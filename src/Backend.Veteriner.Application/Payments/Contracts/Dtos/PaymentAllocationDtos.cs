using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Contracts.Dtos;

/// <summary>POST /payments tahsis satırı: hangi ücrete ne kadar.</summary>
public sealed record PaymentAllocationLineDto(Guid ChargeId, decimal Amount);

/// <summary>Ödeme oluşturma sonucu; <see cref="Created"/> false ise aynı <c>requestId</c> ile tekrar istek ilk ödemeyi döndürmüştür.</summary>
public sealed record PaymentCreateResultDto(Guid Id, bool Created);

/// <summary>GET /payments/{id}/allocations satırı.</summary>
public sealed record PaymentAllocationItemDto(
    Guid Id,
    Guid ChargeId,
    string ChargeDescription,
    decimal Amount,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId);

/// <summary>GET /charges/{id}/allocations satırı.</summary>
public sealed record ChargeAllocationItemDto(
    Guid Id,
    Guid PaymentId,
    DateTime PaidAtUtc,
    PaymentMethod Method,
    decimal Amount,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId);
