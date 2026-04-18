using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetMemberById.Validators;

public sealed class GetTenantMemberByIdQueryValidator : AbstractValidator<GetTenantMemberByIdQuery>
{
    public GetTenantMemberByIdQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
    }
}
