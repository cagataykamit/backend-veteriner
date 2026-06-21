using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.ReadModels;

/// <summary>
/// Query DB <c>PaymentReadModels</c> listesi için okuma isteği.
/// Tenant + clinic kapsamı zorunludur (tenant-wide list desteklenmez).
/// </summary>
public sealed record PaymentsListReadRequest(
    Guid TenantId,
    Guid ClinicId,
    int Page,
    int PageSize,
    Guid? ClientId = null,
    Guid? PetId = null,
    PaymentMethod? Method = null,
    DateTime? PaidFromUtc = null,
    DateTime? PaidToUtc = null,
    string? SearchContainsLikePattern = null);
