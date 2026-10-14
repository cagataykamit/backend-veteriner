using Backend.Veteriner.Application.ExaminationTemplates.Commands.Create;
using Backend.Veteriner.Domain.Catalog;
using FluentValidation;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Update;

public sealed class UpdateExaminationTemplateCommandValidator : AbstractValidator<UpdateExaminationTemplateCommand>
{
    public UpdateExaminationTemplateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ExaminationTemplate.MaxNameLength);
        RuleFor(x => x.VisitReason).MaximumLength(ExaminationTemplate.MaxVisitReasonLength);
        RuleFor(x => x.Findings).MaximumLength(ExaminationTemplate.MaxTextLength);
        RuleFor(x => x.Plan).MaximumLength(ExaminationTemplate.MaxTextLength);
        RuleFor(x => x.Lines).NotNull();
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(ExaminationTemplate.MaxLines).When(x => x.Lines is not null);
        RuleForEach(x => x.Lines).SetValidator(new ExaminationTemplateLineRequestValidator());
    }
}
