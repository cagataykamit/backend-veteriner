using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Commands.Update.Validators;

public sealed class UpdatePetCommandValidator : AbstractValidator<UpdatePetCommand>
{
    public UpdatePetCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.SpeciesId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MinimumLength(1).MaximumLength(200);
        RuleFor(x => x.Breed).MaximumLength(150);
        RuleFor(x => x.BirthDate)
            .Must(d => !d.HasValue || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Dogum tarihi gelecekte olamaz.");
    }
}