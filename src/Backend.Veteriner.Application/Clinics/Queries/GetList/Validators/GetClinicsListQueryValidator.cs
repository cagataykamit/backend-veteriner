using Backend.Veteriner.Application.Clinics.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Queries.GetList.Validators;

public sealed class GetClinicsListQueryValidator : AbstractValidator<GetClinicsListQuery>
{
    public GetClinicsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);
    }
}
