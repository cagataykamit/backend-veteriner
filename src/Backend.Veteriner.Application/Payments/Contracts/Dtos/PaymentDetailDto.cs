using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Contracts.Dtos;

/// <summary>GET /payments/{id} yanıtı. <see cref="PetId"/>, <see cref="AppointmentId"/>, <see cref="ExaminationId"/>, <see cref="Notes"/> null olabilir.</summary>
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
    string? Notes);
