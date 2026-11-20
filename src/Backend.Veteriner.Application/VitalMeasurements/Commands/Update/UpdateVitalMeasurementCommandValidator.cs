using Backend.Veteriner.Domain.VitalMeasurements;
using FluentValidation;

namespace Backend.Veteriner.Application.VitalMeasurements.Commands.Update;

public sealed class UpdateVitalMeasurementCommandValidator : AbstractValidator<UpdateVitalMeasurementCommand>
{
    public UpdateVitalMeasurementCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.MeasuredAtUtc).NotEqual(default(DateTime));
        RuleFor(x => x.Note)
            .MaximumLength(VitalMeasurement.MaxNoteLength)
            .When(x => !string.IsNullOrEmpty(x.Note));
    }
}
