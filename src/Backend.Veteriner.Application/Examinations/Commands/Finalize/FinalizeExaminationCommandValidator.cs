using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Commands.Finalize;

public sealed class FinalizeExaminationCommandValidator : AbstractValidator<FinalizeExaminationCommand>
{
    public FinalizeExaminationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
