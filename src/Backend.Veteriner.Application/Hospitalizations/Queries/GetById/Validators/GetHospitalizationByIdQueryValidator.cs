using Backend.Veteriner.Application.Hospitalizations.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Queries.GetById.Validators;

public sealed class GetHospitalizationByIdQueryValidator : AbstractValidator<GetHospitalizationByIdQuery>
{
    public GetHospitalizationByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
