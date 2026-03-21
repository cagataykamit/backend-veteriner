using Backend.Veteriner.Application.Tenants.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetById.Validators;

public sealed class GetTenantByIdQueryValidator : AbstractValidator<GetTenantByIdQuery>
{
    public GetTenantByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
