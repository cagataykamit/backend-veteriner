using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetAssignableOperationClaimsForInvite.Validators;

public sealed class GetAssignableOperationClaimsForInviteQueryValidator
    : AbstractValidator<GetAssignableOperationClaimsForInviteQuery>
{
    public GetAssignableOperationClaimsForInviteQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
