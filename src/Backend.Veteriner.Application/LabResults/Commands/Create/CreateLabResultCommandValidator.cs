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
            .WithMessage("ExaminationId is invalid.");
        RuleFor(x => x.ResultDateUtc).NotEqual(default(DateTime));
        RuleFor(x => x.TestName).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ResultText).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Interpretation)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Interpretation));
        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
