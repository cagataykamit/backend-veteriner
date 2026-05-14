using FluentValidation;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Update;

public sealed class UpdateVaccinationCommandValidator : AbstractValidator<UpdateVaccinationCommand>
{
    public UpdateVaccinationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.VaccineDefinitionId).NotEmpty();

        RuleFor(x => x.Status)
            .IsInEnum();

        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
