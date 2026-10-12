using FluentValidation;

namespace Backend.Veteriner.Application.Charges.Commands.Correct;

public sealed class CorrectChargeCommandValidator : AbstractValidator<CorrectChargeCommand>
{
    public CorrectChargeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
