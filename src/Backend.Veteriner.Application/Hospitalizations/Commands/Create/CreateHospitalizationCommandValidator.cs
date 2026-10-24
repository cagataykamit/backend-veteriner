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
            .WithMessage("ExaminationId is invalid.");
        RuleFor(x => x.VisitId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("VisitId is invalid.");
        RuleFor(x => x.RoomId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("RoomId is invalid.");
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
