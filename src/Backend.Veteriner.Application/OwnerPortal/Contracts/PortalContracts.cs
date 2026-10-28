using System.Text.Json.Serialization;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.OwnerPortal.Contracts;

// Hasta sahibi portalı yanıtları (docs/PORTAL_API_CONTRACT.md §5). Personel DTO'ları yeniden kullanılmaz; her alan burada seçilir.

/// <summary>Portal oturumu: kısa ömürlü access + döndürülen refresh.</summary>
public sealed record PortalAuthTokensDto(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

/// <summary>Davet kabulü: kurulan bağ ve oturum.</summary>
public sealed record PortalInviteAcceptResultDto(
    Guid LinkId,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

/// <summary>Davet önizlemesi; yalnız e-postaya giden token sahibine görünür.</summary>
public sealed record PortalInvitePreviewDto(
    string ClinicName,
    string EmailMasked,
    DateTime ExpiresAtUtc,
    bool HasAccount,
    IReadOnlyList<LegalDocumentVersionDto> RequiredLegalDocuments);

public sealed record PortalLinkSummaryDto(
    Guid LinkId,
    Guid ClinicId,
    string ClinicName,
    string ClientFullName);

public sealed record PortalMeDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool HasPassword,
    IReadOnlyList<PortalLinkSummaryDto> Links);

public sealed record PortalPendingClinicDisclosureDto(
    Guid LinkId,
    Guid ClinicId,
    string ClinicName);

/// <summary>Bekleyen hukuki kabuller ve onaylanmamış klinik aydınlatma metinleri (ADR-029 + ADR-031 K4).</summary>
public sealed record PortalLegalStatusDto(
    bool RequiresAcceptance,
    IReadOnlyList<LegalDocumentVersionDto> PendingDocuments,
    IReadOnlyList<PortalPendingClinicDisclosureDto> PendingClinicDisclosures);

/// <summary>Kliniğin aydınlatma metni; yer tutucular sunucuda doldurulur.</summary>
public sealed record PortalClinicDisclosureDto(
    Guid LinkId,
    Guid ClinicId,
    string ClinicName,
    string Text,
    bool IsDefault,
    DateTime? AcknowledgedAtUtc);

/// <summary>Klinik paneli: müşterinin portal bağı.</summary>
public sealed record ClientPortalLinkDto(
    Guid LinkId,
    Guid ClinicId,
    string? ClinicName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerAccountClientLinkStatus Status,
    string? AccountEmail,
    DateTime LinkedAtUtc,
    DateTime? ClosedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerAccountClientLinkCloseReason? CloseReason);

/// <summary>Klinik paneli: davet (<c>Status</c> metni <c>Pending</c>/<c>Accepted</c>/<c>Revoked</c>/<c>Expired</c>).</summary>
public sealed record ClientPortalInviteDto(
    Guid InviteId,
    Guid ClinicId,
    string Email,
    string Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? RevokedAtUtc)
{
    public static ClientPortalInviteDto From(OwnerPortalInvite invite, DateTime utcNow) => new(
        invite.Id,
        invite.ClinicId,
        invite.Email,
        invite.Status == OwnerPortalInviteStatus.Pending && invite.IsExpired(utcNow)
            ? "Expired"
            : invite.Status.ToString(),
        invite.CreatedAtUtc,
        invite.ExpiresAtUtc,
        invite.AcceptedAtUtc,
        invite.RevokedAtUtc);
}

/// <summary>Klinik paneli: müşteri kartındaki portal bölümü (docs/PORTAL_API_CONTRACT.md §4.1).</summary>
public sealed record ClientPortalAccessDto(
    Guid ClientId,
    bool CanInvite,
    string? InviteBlockedCode,
    IReadOnlyList<ClientPortalLinkDto> Links,
    IReadOnlyList<ClientPortalInviteDto> Invites);
