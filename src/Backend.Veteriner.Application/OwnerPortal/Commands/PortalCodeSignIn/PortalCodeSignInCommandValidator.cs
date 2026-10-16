using Backend.Veteriner.Domain.OwnerPortal;
using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.PortalCodeSignIn;

public sealed class PortalCodeSignInCommandValidator : AbstractValidator<PortalCodeSignInCommand>
{
    public PortalCodeSignInCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(OwnerAccount.MaxEmailLength);
        RuleFor(x => x.OneTimeCode).NotEmpty().MaximumLength(20);
    }
}
