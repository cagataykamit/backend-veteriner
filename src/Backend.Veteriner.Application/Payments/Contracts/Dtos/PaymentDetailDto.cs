using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Contracts.Dtos;

public sealed record PaymentDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid ClientId,
    Guid? PetId,
    Guid? AppointmentId,
    Guid? ExaminationId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    DateTime PaidAtUtc,
    string? Notes);
