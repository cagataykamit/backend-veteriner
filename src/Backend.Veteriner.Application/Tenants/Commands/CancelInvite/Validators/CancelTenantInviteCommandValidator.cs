using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.CancelInvite.Validators;

public sealed class CancelTenantInviteCommandValidator : AbstractValidator<CancelTenantInviteCommand>
{
    public CancelTenantInviteCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.InviteId).NotEmpty();
    }
}
