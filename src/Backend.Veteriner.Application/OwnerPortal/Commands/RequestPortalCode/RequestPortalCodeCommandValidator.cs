using Backend.Veteriner.Domain.OwnerPortal;
using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.RequestPortalCode;

public sealed class RequestPortalCodeCommandValidator : AbstractValidator<RequestPortalCodeCommand>
{
    public RequestPortalCodeCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(OwnerAccount.MaxEmailLength);
        RuleFor(x => x.Purpose).IsInEnum();
    }
}
