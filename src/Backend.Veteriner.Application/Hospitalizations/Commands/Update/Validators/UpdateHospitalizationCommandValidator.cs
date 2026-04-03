using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Update.Validators;

public sealed class UpdateHospitalizationCommandValidator : AbstractValidator<UpdateHospitalizationCommand>
{
    public UpdateHospitalizationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ExaminationId is invalid.");
        RuleFor(x => x.AdmittedAtUtc).NotEqual(default(DateTime));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
