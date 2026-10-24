using Backend.Veteriner.Domain.Hospitalizations;
using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.UpdateWard;

public sealed class UpdateWardCommandValidator : AbstractValidator<UpdateWardCommand>
{
    public UpdateWardCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.WardId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(HospitalizationWard.NameMaxLength);
    }
}
