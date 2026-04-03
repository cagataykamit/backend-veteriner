using Backend.Veteriner.Application.Prescriptions.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Prescriptions.Queries.GetById.Validators;

public sealed class GetPrescriptionByIdQueryValidator : AbstractValidator<GetPrescriptionByIdQuery>
{
    public GetPrescriptionByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
