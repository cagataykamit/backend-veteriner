using FluentValidation;

namespace Backend.Veteriner.Application.Visits.Commands.Create;

public sealed class CreateVisitCommandValidator : AbstractValidator<CreateVisitCommand>
{
    public CreateVisitCommandValidator()
    {
        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ClinicId gecersiz.");

        RuleFor(x => x.PetId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("PetId gecersiz.");

        RuleFor(x => x.AppointmentId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("AppointmentId gecersiz.");

        RuleFor(x => x.ResponsibleVeterinarianUserId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ResponsibleVeterinarianUserId gecersiz.");
    }
}
