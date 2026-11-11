using Backend.Veteriner.Application.ExaminationTemplates.Commands.Create;
using Backend.Veteriner.Domain.Catalog;
using FluentValidation;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Apply;

public sealed class ApplyExaminationTemplateCommandValidator : AbstractValidator<ApplyExaminationTemplateCommand>
{
    public ApplyExaminationTemplateCommandValidator()
    {
        RuleFor(x => x.ExaminationId).NotEmpty();
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.Lines!.Count).LessThanOrEqualTo(ExaminationTemplate.MaxLines).When(x => x.Lines is not null);
        RuleForEach(x => x.Lines).SetValidator(new ExaminationTemplateLineRequestValidator());
    }
}
