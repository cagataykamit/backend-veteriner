using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Commands.Update.Validators;

public sealed class UpdateExaminationCommandValidator : AbstractValidator<UpdateExaminationCommand>
{
    public UpdateExaminationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir klinik seçin.");

        RuleFor(x => x.PetId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir hasta seçin.");

        RuleFor(x => x.AppointmentId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir randevu seçin.");

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

        RuleFor(x => x.RowVersion)
            .NotEmpty()
            .WithMessage("Kayıt sürümü eksik ya da geçersiz. Sayfayı yenileyip tekrar deneyin.");
    }
}
