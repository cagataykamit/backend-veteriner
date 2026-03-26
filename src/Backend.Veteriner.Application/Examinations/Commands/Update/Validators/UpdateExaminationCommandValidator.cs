using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Commands.Update.Validators;

public sealed class UpdateExaminationCommandValidator : AbstractValidator<UpdateExaminationCommand>
{
    public UpdateExaminationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ClinicId gecersiz.");

        RuleFor(x => x.PetId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("PetId gecersiz.");

        RuleFor(x => x.AppointmentId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("AppointmentId gecersiz.");

        RuleFor(x => x)
            .Must(x =>
                x.AppointmentId is { } aid && aid != Guid.Empty
                || (x.ClinicId is { } cid && cid != Guid.Empty) && (x.PetId is { } pid && pid != Guid.Empty))
            .WithMessage("AppointmentId veya ClinicId+PetId zorunludur.");

        RuleFor(x => x.ExaminedAtUtc).NotEqual(default(DateTime));

        RuleFor(x => x.VisitReason)
            .NotEmpty()
            .MaximumLength(2000);

        RuleFor(x => x.Findings)
            .NotEmpty()
            .MaximumLength(8000);

        RuleFor(x => x.Assessment)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Assessment));

        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}

