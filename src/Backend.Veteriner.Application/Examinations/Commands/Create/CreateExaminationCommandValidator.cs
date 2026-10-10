using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Commands.Create;

public sealed class CreateExaminationCommandValidator : AbstractValidator<CreateExaminationCommand>
{
    public CreateExaminationCommandValidator()
    {
        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ClinicId gecersiz.");

        RuleFor(x => x.PetId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("PetId gecersiz.");

        RuleFor(x => x)
            .Must(x =>
                x.VisitId is { } vid && vid != Guid.Empty
                || x.AppointmentId is { } aid && aid != Guid.Empty
                || (x.ClinicId is { } cid && cid != Guid.Empty) && (x.PetId is { } pid && pid != Guid.Empty))
            .WithMessage("VisitId, AppointmentId veya ClinicId+PetId zorunludur.");

        RuleFor(x => x.VisitId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("VisitId gecersiz.");

        RuleFor(x => x.AppointmentId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("AppointmentId gecersiz.");

        RuleFor(x => x.ExaminedAtUtc).NotEqual(default(DateTime));

        RuleFor(x => x.VisitReason)
            .NotEmpty()
            .MaximumLength(2000);

        RuleFor(x => x.Findings)
            .MaximumLength(8000);

        RuleFor(x => x.Anamnesis)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Anamnesis));

        RuleFor(x => x.Plan)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Plan));

        RuleFor(x => x.Assessment)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Assessment));

        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));

        RuleFor(x => x.WeightKg)
            .GreaterThan(0)
            .When(x => x.WeightKg.HasValue);

        RuleFor(x => x.TemperatureC)
            .GreaterThan(0)
            .When(x => x.TemperatureC.HasValue);

        RuleFor(x => x.HeartRateBpm)
            .GreaterThan(0)
            .When(x => x.HeartRateBpm.HasValue);

        RuleFor(x => x.RespiratoryRatePerMin)
            .GreaterThan(0)
            .When(x => x.RespiratoryRatePerMin.HasValue);
    }
}
