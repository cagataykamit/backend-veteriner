using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Contracts.Dtos;

/// <summary>GET /payments liste öğesi. <see cref="PetId"/> hayvan yoksa null; <see cref="PetName"/> o zaman boş string olabilir. <see cref="PetCount"/> farklı hayvan sayısıdır (>1 ise <see cref="PetId"/> null); Query DB yolunda null.</summary>
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
    DateTime PaidAtUtc,
    int? PetCount = null);
