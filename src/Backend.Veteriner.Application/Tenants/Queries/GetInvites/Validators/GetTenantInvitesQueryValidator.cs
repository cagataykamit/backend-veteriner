using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetInvites.Validators;

public sealed class GetTenantInvitesQueryValidator : AbstractValidator<GetTenantInvitesQuery>
{
    public GetTenantInvitesQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
