using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;

namespace Backend.Veteriner.Application.Public.Contracts.Dtos;

/// <param name="Discount">Kayıtta kullanılan satış ortağı kodu (ADR-037); kod yoksa <c>null</c>.</param>
/// <param name="ExampleClinic">Kayıtta yüklenen Örnek Veteriner Kliniği özeti (ADR-002); boş klinikle başlandıysa <c>null</c>.</param>
public sealed record PublicOwnerSignupResultDto(
    Guid TenantId,
    Guid ClinicId,
    Guid UserId,
    string PlanCode,
    DateTime TrialStartsAtUtc,
    DateTime TrialEndsAtUtc,
    bool CanLogin,
    string NextStep,
    DiscountCodeSummaryDto? Discount = null,
    ExampleClinicLoadSummaryDto? ExampleClinic = null);
