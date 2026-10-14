using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Commands.SetStatus;

public sealed class SetPetStatusCommandValidator : AbstractValidator<SetPetStatusCommand>
{
    public SetPetStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}
