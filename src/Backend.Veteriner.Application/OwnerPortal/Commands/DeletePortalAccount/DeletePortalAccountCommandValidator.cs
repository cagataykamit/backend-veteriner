using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.DeletePortalAccount;

public sealed class DeletePortalAccountCommandValidator : AbstractValidator<DeletePortalAccountCommand>
{
    public DeletePortalAccountCommandValidator()
    {
        RuleFor(x => x.OneTimeCode).NotEmpty().MaximumLength(20);
    }
}
