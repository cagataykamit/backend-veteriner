using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.ResendInvite.Validators;

public sealed class ResendTenantInviteCommandValidator : AbstractValidator<ResendTenantInviteCommand>
{
    public ResendTenantInviteCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.InviteId).NotEmpty();
    }
}
