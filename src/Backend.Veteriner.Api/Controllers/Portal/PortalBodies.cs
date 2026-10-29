using Backend.Veteriner.Application.LegalAcceptances;

namespace Backend.Veteriner.Api.Controllers.Portal;

// Hasta sahibi portalı istek gövdeleri (docs/PORTAL_API_CONTRACT.md §5).

public sealed class PortalInviteAcceptBody
{
    public string? DisplayName { get; init; }

    /// <summary>Opsiyonel; verilmezse hesap kodla girer.</summary>
    public string? Password { get; init; }

    public IReadOnlyList<LegalDocumentAcceptanceInput>? AcceptedLegalDocuments { get; init; }
}

public sealed class PortalEmailBody
{
    public string? Email { get; init; }
}

public sealed class PortalCodeSignInBody
{
    public string? Email { get; init; }
    public string? OneTimeCode { get; init; }
}

public sealed class PortalPasswordSignInBody
{
    public string? Email { get; init; }
    public string? Password { get; init; }
}

public sealed class PortalPasswordResetConfirmBody
{
    public string? Email { get; init; }
    public string? OneTimeCode { get; init; }
    public string? NewPassword { get; init; }
}

public sealed class PortalRefreshBody
{
    public string? RefreshToken { get; init; }
}

public sealed class PortalAccountDeletionBody
{
    public string? OneTimeCode { get; init; }
}

public sealed class PortalLegalAcceptBody
{
    public IReadOnlyList<LegalDocumentAcceptanceInput>? Documents { get; init; }
}

public sealed class PortalReminderPreferencesBody
{
    /// <summary>Zorunlu: true = hatırlatma istemiyorum.</summary>
    public bool? RemindersOptedOut { get; init; }
}
