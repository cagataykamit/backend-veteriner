using Backend.Veteriner.Domain.Hospitalizations;
using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateWard;

public sealed class CreateWardCommandValidator : AbstractValidator<CreateWardCommand>
{
    public CreateWardCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(HospitalizationWard.NameMaxLength);
    }
}
