using Backend.Veteriner.Application.LegalAcceptances;
using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.AcceptPortalLegalDocuments;

public sealed class AcceptPortalLegalDocumentsCommandValidator : AbstractValidator<AcceptPortalLegalDocumentsCommand>
{
    public AcceptPortalLegalDocumentsCommandValidator()
    {
        RuleFor(x => x.Documents).NotEmpty();
        RuleFor(x => x.Documents)
            .Must(LegalDocumentInputValidation.HasNoDuplicates)
            .WithMessage(LegalDocumentInputValidation.DuplicateMessage);
        RuleForEach(x => x.Documents).SetValidator(new LegalDocumentAcceptanceInputValidator());
    }
}
