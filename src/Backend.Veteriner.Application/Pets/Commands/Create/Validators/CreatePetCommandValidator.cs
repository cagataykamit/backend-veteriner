using Backend.Veteriner.Application.Pets.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Commands.Create.Validators;

public sealed class CreatePetCommandValidator : AbstractValidator<CreatePetCommand>
{
    public CreatePetCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MinimumLength(1).MaximumLength(200);
        RuleFor(x => x.Species).NotEmpty().MinimumLength(2).MaximumLength(100);
        RuleFor(x => x.Breed).MaximumLength(150);
        RuleFor(x => x.BirthDate)
            .Must(d => !d.HasValue || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Doğum tarihi gelecekte olamaz.");
    }
}
