using Backend.Veteriner.Application.Treatments.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Treatments.Queries.GetById.Validators;

public sealed class GetTreatmentByIdQueryValidator : AbstractValidator<GetTreatmentByIdQuery>
{
    public GetTreatmentByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
