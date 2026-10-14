using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Domain.Catalog;
using FluentValidation;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Create;

public sealed class CreateExaminationTemplateCommandValidator : AbstractValidator<CreateExaminationTemplateCommand>
{
    public CreateExaminationTemplateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ExaminationTemplate.MaxNameLength);
        RuleFor(x => x.VisitReason).MaximumLength(ExaminationTemplate.MaxVisitReasonLength);
        RuleFor(x => x.Findings).MaximumLength(ExaminationTemplate.MaxTextLength);
        RuleFor(x => x.Plan).MaximumLength(ExaminationTemplate.MaxTextLength);
        RuleFor(x => x.Lines!.Count).LessThanOrEqualTo(ExaminationTemplate.MaxLines).When(x => x.Lines is not null);
        RuleForEach(x => x.Lines).SetValidator(new ExaminationTemplateLineRequestValidator());
    }
}

/// <summary>Satır biçim kontrolü (ekle ve güncelle ortak); miktar ölçeği ve ürün kuralları domain'de (<see cref="ExaminationTemplate"/>).</summary>
public sealed class ExaminationTemplateLineRequestValidator : AbstractValidator<ExaminationTemplateLineRequest>
{
    public ExaminationTemplateLineRequestValidator()
    {
        RuleFor(l => l.ProductId).NotEmpty();
        RuleFor(l => l.Quantity).GreaterThan(0);
    }
}
