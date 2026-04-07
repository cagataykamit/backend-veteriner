using FluentValidation;

namespace Backend.Veteriner.Application.Public.Commands.AcceptInvite.Validators;

public sealed class AcceptTenantInviteCommandValidator : AbstractValidator<AcceptTenantInviteCommand>
{
    public AcceptTenantInviteCommandValidator()
    {
        RuleFor(x => x.RawToken).NotEmpty();
        RuleFor(x => x.CurrentUserId).NotEmpty();
    }
}
