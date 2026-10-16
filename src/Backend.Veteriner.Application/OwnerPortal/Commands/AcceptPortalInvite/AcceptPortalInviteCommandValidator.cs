using Backend.Veteriner.Application.Common.Validation;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.OwnerPortal;
using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.AcceptPortalInvite;

public sealed class AcceptPortalInviteCommandValidator : AbstractValidator<AcceptPortalInviteCommand>
{
    public AcceptPortalInviteCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DisplayName).MaximumLength(OwnerAccount.MaxDisplayNameLength);
        RuleFor(x => x.Password!).StrongPasswordRules(includeMaxLength: true)
            .When(x => !string.IsNullOrEmpty(x.Password));
        RuleFor(x => x.AcceptedLegalDocuments)
            .Must(LegalDocumentInputValidation.HasNoDuplicates)
            .WithMessage(LegalDocumentInputValidation.DuplicateMessage);
        RuleForEach(x => x.AcceptedLegalDocuments).SetValidator(new LegalDocumentAcceptanceInputValidator());
    }
}
