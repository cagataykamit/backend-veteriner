using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Commands.AddAddendum;

public sealed class AddExaminationAddendumCommandValidator : AbstractValidator<AddExaminationAddendumCommand>
{
    public AddExaminationAddendumCommandValidator()
    {
        RuleFor(x => x.ExaminationId).NotEmpty();
    }
}
