using Backend.Veteriner.Application.Common.Validation;
using Backend.Veteriner.Domain.OwnerPortal;
using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.PortalPasswordSignIn;

public sealed class PortalPasswordSignInCommandValidator : AbstractValidator<PortalPasswordSignInCommand>
{
    public PortalPasswordSignInCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(OwnerAccount.MaxEmailLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordValidationExtensions.MaxPasswordLength);
    }
}
