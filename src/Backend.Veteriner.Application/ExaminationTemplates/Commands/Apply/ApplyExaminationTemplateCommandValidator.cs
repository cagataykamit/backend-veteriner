using FluentValidation;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Apply;

public sealed class ApplyExaminationTemplateCommandValidator : AbstractValidator<ApplyExaminationTemplateCommand>
{
    public ApplyExaminationTemplateCommandValidator()
    {
        RuleFor(x => x.ExaminationId).NotEmpty();
        RuleFor(x => x.TemplateId).NotEmpty();
    }
}
