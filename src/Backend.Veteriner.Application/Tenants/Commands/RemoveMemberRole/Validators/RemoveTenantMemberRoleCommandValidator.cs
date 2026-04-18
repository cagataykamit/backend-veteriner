using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.RemoveMemberRole.Validators;

public sealed class RemoveTenantMemberRoleCommandValidator : AbstractValidator<RemoveTenantMemberRoleCommand>
{
    public RemoveTenantMemberRoleCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.OperationClaimId).NotEmpty();
    }
}
