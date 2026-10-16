using Backend.Veteriner.Application.Common.Validation;
using Backend.Veteriner.Domain.OwnerPortal;
using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.ConfirmPortalPasswordReset;

public sealed class ConfirmPortalPasswordResetCommandValidator : AbstractValidator<ConfirmPortalPasswordResetCommand>
{
    public ConfirmPortalPasswordResetCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(OwnerAccount.MaxEmailLength);
        RuleFor(x => x.OneTimeCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.NewPassword).NotEmpty().StrongPasswordRules(includeMaxLength: true);
    }
}
