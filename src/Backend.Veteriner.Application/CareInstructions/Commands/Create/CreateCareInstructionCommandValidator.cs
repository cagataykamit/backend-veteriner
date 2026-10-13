using Backend.Veteriner.Domain.CareInstructions;
using FluentValidation;

namespace Backend.Veteriner.Application.CareInstructions.Commands.Create;

public sealed class CreateCareInstructionCommandValidator : AbstractValidator<CreateCareInstructionCommand>
{
    public CreateCareInstructionCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.HospitalizationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("HospitalizationId is invalid.");
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ExaminationId is invalid.");
        RuleFor(x => x.VisitId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("VisitId is invalid.");
        RuleFor(x => x.Text).NotEmpty().MaximumLength(CareInstruction.MaxTextLength);
    }
}
