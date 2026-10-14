using FluentValidation;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.SetActive;

public sealed class SetExaminationTemplateActiveCommandValidator : AbstractValidator<SetExaminationTemplateActiveCommand>
{
    public SetExaminationTemplateActiveCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
