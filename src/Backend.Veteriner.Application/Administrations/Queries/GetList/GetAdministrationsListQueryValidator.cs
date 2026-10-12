using FluentValidation;

namespace Backend.Veteriner.Application.Administrations.Queries.GetList;

public sealed class GetAdministrationsListQueryValidator : AbstractValidator<GetAdministrationsListQuery>
{
    public GetAdministrationsListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}
