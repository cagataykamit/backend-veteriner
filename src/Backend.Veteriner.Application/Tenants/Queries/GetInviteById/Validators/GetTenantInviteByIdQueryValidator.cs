using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetInviteById.Validators;

public sealed class GetTenantInviteByIdQueryValidator : AbstractValidator<GetTenantInviteByIdQuery>
{
    public GetTenantInviteByIdQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.InviteId).NotEmpty();
    }
}
