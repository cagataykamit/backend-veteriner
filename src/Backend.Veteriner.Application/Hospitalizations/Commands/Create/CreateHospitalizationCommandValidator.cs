using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Create;

public sealed class CreateHospitalizationCommandValidator : AbstractValidator<CreateHospitalizationCommand>
{
    public CreateHospitalizationCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir muayene seçin.");
        RuleFor(x => x.VisitId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir geliş seçin.");
        RuleFor(x => x.RoomId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir oda seçin.");
        RuleFor(x => x.AdmittedAtUtc).NotEqual(default(DateTime));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Location)
            .MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.Location));
        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
