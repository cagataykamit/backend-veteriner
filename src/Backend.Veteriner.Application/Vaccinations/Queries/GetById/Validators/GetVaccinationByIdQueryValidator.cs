using Backend.Veteriner.Application.Vaccinations.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetById.Validators;

public sealed class GetVaccinationByIdQueryValidator : AbstractValidator<GetVaccinationByIdQuery>
{
    public GetVaccinationByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
