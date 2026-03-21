using Backend.Veteriner.Application.Clinics.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Queries.GetById.Validators;

public sealed class GetClinicByIdQueryValidator : AbstractValidator<GetClinicByIdQuery>
{
    public GetClinicByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
