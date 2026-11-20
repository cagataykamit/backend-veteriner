using FluentValidation;

namespace Backend.Veteriner.Application.VitalMeasurements.Commands.Cancel;

public sealed class CancelVitalMeasurementCommandValidator : AbstractValidator<CancelVitalMeasurementCommand>
{
    public CancelVitalMeasurementCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
