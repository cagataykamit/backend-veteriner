using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Queries.GetOpenItems.Validators;

public sealed class GetPetOpenItemsQueryValidator : AbstractValidator<GetPetOpenItemsQuery>
{
    public GetPetOpenItemsQueryValidator()
    {
        RuleFor(x => x.PetId).NotEmpty();
    }
}
