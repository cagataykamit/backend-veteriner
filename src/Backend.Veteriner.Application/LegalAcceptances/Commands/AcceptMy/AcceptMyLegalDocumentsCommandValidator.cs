using FluentValidation;

namespace Backend.Veteriner.Application.LegalAcceptances.Commands.AcceptMy;

public sealed class AcceptMyLegalDocumentsCommandValidator : AbstractValidator<AcceptMyLegalDocumentsCommand>
{
    public AcceptMyLegalDocumentsCommandValidator()
    {
        RuleFor(x => x.Documents).NotEmpty();
        RuleFor(x => x.Documents)
            .Must(LegalDocumentInputValidation.HasNoDuplicates)
            .WithMessage(LegalDocumentInputValidation.DuplicateMessage);
        RuleForEach(x => x.Documents).SetValidator(new LegalDocumentAcceptanceInputValidator());
    }
}
