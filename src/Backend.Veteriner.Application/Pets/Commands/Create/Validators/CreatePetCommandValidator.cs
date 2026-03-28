using Backend.Veteriner.Application.Pets.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Commands.Create.Validators;

public sealed class CreatePetCommandValidator : AbstractValidator<CreatePetCommand>
{
    public CreatePetCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.SpeciesId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MinimumLength(1).MaximumLength(200);
        RuleFor(x => x.Breed).MaximumLength(150);
        RuleFor(x => x.Gender!.Value)
            .IsInEnum()
            .When(x => x.Gender.HasValue);
        RuleFor(x => x.BirthDate)
            .Must(d => !d.HasValue || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Doğum tarihi gelecekte olamaz.");
    }
}
