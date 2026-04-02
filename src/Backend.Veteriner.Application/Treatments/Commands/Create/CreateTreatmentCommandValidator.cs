using FluentValidation;

namespace Backend.Veteriner.Application.Treatments.Commands.Create;

public sealed class CreateTreatmentCommandValidator : AbstractValidator<CreateTreatmentCommand>
{
    public CreateTreatmentCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ExaminationId is invalid.");
        RuleFor(x => x.TreatmentDateUtc).NotEqual(default(DateTime));
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
