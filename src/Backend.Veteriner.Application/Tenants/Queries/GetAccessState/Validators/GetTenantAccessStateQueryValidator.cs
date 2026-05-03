using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetAccessState.Validators;

public sealed class GetTenantAccessStateQueryValidator : AbstractValidator<GetTenantAccessStateQuery>
{
    public GetTenantAccessStateQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
