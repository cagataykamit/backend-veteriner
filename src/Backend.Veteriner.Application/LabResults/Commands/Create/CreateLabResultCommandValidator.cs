using Backend.Veteriner.Application.LabResults.Lines;
using Backend.Veteriner.Domain.LabResults;
using FluentValidation;

namespace Backend.Veteriner.Application.LabResults.Commands.Create;

public sealed class CreateLabResultCommandValidator : AbstractValidator<CreateLabResultCommand>
{
    public CreateLabResultCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir muayene seçin.");
        RuleFor(x => x.VisitId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir geliş seçin.");
        RuleFor(x => x.ResultDateUtc)
            .NotEqual(default(DateTime))
            .When(x => x.ResultDateUtc.HasValue);
        RuleFor(x => x.TestName).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ResultText)
            .MaximumLength(8000)
            .When(x => !string.IsNullOrEmpty(x.ResultText));
        RuleFor(x => x.Interpretation)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Interpretation));
        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
        RuleFor(x => x.Lines)
            .Must(lines => lines!.Count <= LabResultLine.MaxPerResult)
            .WithMessage($"Bir lab sonucunda en fazla {LabResultLine.MaxPerResult} değer satırı olabilir.")
            .When(x => x.Lines is not null);
        RuleForEach(x => x.Lines).NotNull().SetValidator(new LabResultLineRequestValidator())
            .When(x => x.Lines is not null);
    }
}
