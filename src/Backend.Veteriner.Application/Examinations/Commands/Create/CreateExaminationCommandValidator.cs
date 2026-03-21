using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Commands.Create;

public sealed class CreateExaminationCommandValidator : AbstractValidator<CreateExaminationCommand>
{
    public CreateExaminationCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ExaminedAtUtc).NotEqual(default(DateTime));

        RuleFor(x => x.VisitReason)
            .NotEmpty()
            .MaximumLength(2000);

        RuleFor(x => x.Findings)
            .NotEmpty()
            .MaximumLength(8000);

        RuleFor(x => x.Assessment)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Assessment));

        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
