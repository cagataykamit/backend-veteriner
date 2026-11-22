using Backend.Veteriner.Application.LegalAcceptances;

namespace Backend.Veteriner.Api.Contracts;

public sealed class SignupAndAcceptInviteBody
{
    public string Password { get; set; } = default!;

    /// <summary>Katılan personelin ad-soyadı; verilmezse e-postanın baş kısmı görünür.</summary>
    public string? FullName { get; set; }

    /// <summary>Davet detayındaki <c>requiredLegalDocuments</c> belgelerinin kabul edilen sürümleri (ADR-029).</summary>
    public List<LegalDocumentAcceptanceInput>? AcceptedLegalDocuments { get; set; }
}
