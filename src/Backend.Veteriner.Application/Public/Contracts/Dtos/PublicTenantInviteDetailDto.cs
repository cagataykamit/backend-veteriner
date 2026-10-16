using Backend.Veteriner.Application.LegalAcceptances;

namespace Backend.Veteriner.Application.Public.Contracts.Dtos;

/// <param name="RequiredLegalDocuments">Davetteki role göre yeni hesabın kabul etmesi gereken belgeler ve güncel sürümleri (ADR-029).</param>
public sealed record PublicTenantInviteDetailDto(
    string InviteToken,
    Guid TenantId,
    string TenantName,
    Guid ClinicId,
    string ClinicName,
    string Email,
    DateTime ExpiresAtUtc,
    bool IsExpired,
    bool IsPending,
    bool CanJoin,
    bool RequiresLogin,
    bool RequiresSignup,
    IReadOnlyList<LegalDocumentVersionDto> RequiredLegalDocuments);
