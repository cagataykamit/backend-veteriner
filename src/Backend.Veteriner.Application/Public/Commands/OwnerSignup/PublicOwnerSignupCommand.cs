using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Public.Commands.OwnerSignup;

/// <param name="AcceptedLegalDocuments">Kayıt formunda kabul edilen belgeler ve sürümleri; üç belgenin güncel sürümü zorunlu (ADR-029).</param>
/// <param name="DiscountCode">İsteğe bağlı satış ortağı kodu (ADR-037); sunucuda yeniden doğrulanır, geçersizse kayıt yapılmaz.</param>
public sealed record PublicOwnerSignupCommand(
    string PlanCode,
    string TenantName,
    string ClinicName,
    string ClinicCity,
    string Email,
    string Password,
    IReadOnlyList<LegalDocumentAcceptanceInput>? AcceptedLegalDocuments = null,
    string? DiscountCode = null) : IRequest<Result<PublicOwnerSignupResultDto>>;
