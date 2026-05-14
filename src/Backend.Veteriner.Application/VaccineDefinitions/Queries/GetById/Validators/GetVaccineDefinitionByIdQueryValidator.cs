using Backend.Veteriner.Application.VaccineDefinitions.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.VaccineDefinitions.Queries.GetById.Validators;

public sealed class GetVaccineDefinitionByIdQueryValidator : AbstractValidator<GetVaccineDefinitionByIdQuery>
{
    public GetVaccineDefinitionByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
