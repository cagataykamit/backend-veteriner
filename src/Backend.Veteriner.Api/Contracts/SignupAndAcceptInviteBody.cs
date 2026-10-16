using Backend.Veteriner.Application.LegalAcceptances;

namespace Backend.Veteriner.Api.Contracts;

public sealed class SignupAndAcceptInviteBody
{
    public string Password { get; set; } = default!;

    /// <summary>Davet detayındaki <c>requiredLegalDocuments</c> belgelerinin kabul edilen sürümleri (ADR-029).</summary>
    public List<LegalDocumentAcceptanceInput>? AcceptedLegalDocuments { get; set; }
}
