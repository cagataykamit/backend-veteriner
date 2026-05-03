using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.RemoveMember.Validators;

public sealed class RemoveTenantMemberCommandValidator : AbstractValidator<RemoveTenantMemberCommand>
{
    public RemoveTenantMemberCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty();
        RuleFor(x => x.MemberId)
            .NotEmpty();
    }
}
