using Backend.Veteriner.Domain.Legal;
using FluentValidation;

namespace Backend.Veteriner.Application.LegalAcceptances;

/// <summary>Kabul gövdesindeki tek belge öğesinin biçimi — personel ve portal kabul uçları aynı kuralı kullanır.</summary>
public sealed class LegalDocumentAcceptanceInputValidator : AbstractValidator<LegalDocumentAcceptanceInput>
{
    public LegalDocumentAcceptanceInputValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.Version).NotEmpty().MaximumLength(LegalAcceptance.MaxVersionLength);
    }
}

public static class LegalDocumentInputValidation
{
    public const string DuplicateMessage = "Aynı belge birden fazla gönderilemez.";

    /// <summary>Aynı belge türü listede bir kez geçer (boş/null liste geçerli sayılır).</summary>
    public static bool HasNoDuplicates(IReadOnlyList<LegalDocumentAcceptanceInput>? documents)
        => documents is null || documents.Select(x => x.DocumentType).Distinct().Count() == documents.Count;
}
