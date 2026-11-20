using Backend.Veteriner.Domain.VitalMeasurements;
using FluentValidation;

namespace Backend.Veteriner.Application.VitalMeasurements.Commands.Create;

public sealed class CreateVitalMeasurementCommandValidator : AbstractValidator<CreateVitalMeasurementCommand>
{
    public CreateVitalMeasurementCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.HospitalizationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir yatış seçin.");
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir muayene seçin.");
        RuleFor(x => x)
            .Must(x => !(x.HospitalizationId.HasValue && x.ExaminationId.HasValue))
            .WithMessage("Ölçüm yatışa ya da muayeneye bağlanır; ikisine birden bağlanamaz.");
        RuleFor(x => x.MeasuredAtUtc).NotEqual(default(DateTime));
        RuleFor(x => x.Note)
            .MaximumLength(VitalMeasurement.MaxNoteLength)
            .When(x => !string.IsNullOrEmpty(x.Note));
    }
}
