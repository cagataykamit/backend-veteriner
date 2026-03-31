using Backend.Veteriner.Application.Pets.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Commands.Create.Validators;

public sealed class CreatePetCommandValidator : AbstractValidator<CreatePetCommand>
{
    public CreatePetCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.SpeciesId).NotEmpty();
        RuleFor(x => x.ColorId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ColorId geçersiz.");
        RuleFor(x => x.Name).NotEmpty().MinimumLength(1).MaximumLength(200);
        RuleFor(x => x.Breed).MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Gender!.Value)
            .IsInEnum()
            .When(x => x.Gender.HasValue);
        RuleFor(x => x.BirthDate)
            .Must(d => !d.HasValue || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Doğum tarihi gelecekte olamaz.");
        RuleFor(x => x.Weight)
            .GreaterThanOrEqualTo(0).When(x => x.Weight.HasValue)
            .WithMessage("Weight negatif olamaz.");
    }
}
