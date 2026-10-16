using Backend.Veteriner.Application.LegalAcceptances;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>PUT /me/display-name gövdesi.</summary>
public sealed class SetMyDisplayNameBody
{
    public string? DisplayName { get; init; }
}

/// <summary>POST /me/legal-acceptances gövdesi.</summary>
public sealed class AcceptMyLegalDocumentsBody
{
    public List<LegalDocumentAcceptanceInput>? Documents { get; init; }
}
