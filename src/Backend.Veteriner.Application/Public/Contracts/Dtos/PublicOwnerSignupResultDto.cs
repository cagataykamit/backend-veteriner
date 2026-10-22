using Backend.Veteriner.Application.SalesPartners.Contracts;

namespace Backend.Veteriner.Application.Public.Contracts.Dtos;

/// <param name="Discount">Kayıtta kullanılan satış ortağı kodu (ADR-037); kod yoksa <c>null</c>.</param>
public sealed record PublicOwnerSignupResultDto(
    Guid TenantId,
    Guid ClinicId,
    Guid UserId,
    string PlanCode,
    DateTime TrialStartsAtUtc,
    DateTime TrialEndsAtUtc,
    bool CanLogin,
    string NextStep,
    DiscountCodeSummaryDto? Discount = null);
