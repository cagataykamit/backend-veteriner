using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.AssignMemberRole.Validators;

public sealed class AssignTenantMemberRoleCommandValidator : AbstractValidator<AssignTenantMemberRoleCommand>
{
    public AssignTenantMemberRoleCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.OperationClaimId).NotEmpty();
    }
}
