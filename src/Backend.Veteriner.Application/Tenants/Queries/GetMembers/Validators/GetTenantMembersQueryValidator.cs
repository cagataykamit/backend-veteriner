using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetMembers.Validators;

public sealed class GetTenantMembersQueryValidator : AbstractValidator<GetTenantMembersQuery>
{
    public GetTenantMembersQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
