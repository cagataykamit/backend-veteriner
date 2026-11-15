using Backend.Veteriner.Domain.Visits;
using FluentValidation;

namespace Backend.Veteriner.Application.Visits.Commands.Create;

public sealed class CreateVisitCommandValidator : AbstractValidator<CreateVisitCommand>
{
    public CreateVisitCommandValidator()
    {
        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir klinik seçin.");

        RuleFor(x => x.PetId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir hasta seçin.");

        RuleFor(x => x.AppointmentId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir randevu seçin.");

        RuleFor(x => x.ResponsibleVeterinarianUserId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir sorumlu hekim seçin.");

        RuleFor(x => x.ArrivalReason).MaximumLength(Visit.MaxArrivalReasonLength);

        RuleFor(x => x.PreparedExaminationTemplateId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir muayene şablonu seçin.");
    }
}
