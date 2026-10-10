using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Queries.GetTimeline.Validators;

public sealed class GetPetTimelineQueryValidator : AbstractValidator<GetPetTimelineQuery>
{
    public GetPetTimelineQueryValidator()
    {
        RuleFor(x => x.PetId).NotEmpty();
    }
}
