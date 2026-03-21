using Backend.Veteriner.Application.Pets.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Queries.GetById.Validators;

public sealed class GetPetByIdQueryValidator : AbstractValidator<GetPetByIdQuery>
{
    public GetPetByIdQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}
