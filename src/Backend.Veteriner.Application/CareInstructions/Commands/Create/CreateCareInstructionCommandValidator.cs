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
            .WithMessage("Geçerli bir yatış seçin.");
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir muayene seçin.");
        RuleFor(x => x.VisitId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir geliş seçin.");
        RuleFor(x => x.Text).NotEmpty().MaximumLength(CareInstruction.MaxTextLength);
    }
}
