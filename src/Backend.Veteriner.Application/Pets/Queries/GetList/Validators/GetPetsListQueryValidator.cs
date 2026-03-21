using Backend.Veteriner.Application.Pets.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Queries.GetList.Validators;

public sealed class GetPetsListQueryValidator : AbstractValidator<GetPetsListQuery>
{
    public GetPetsListQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
