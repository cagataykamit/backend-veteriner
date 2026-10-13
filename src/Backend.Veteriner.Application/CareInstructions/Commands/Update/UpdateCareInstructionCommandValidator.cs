using Backend.Veteriner.Domain.CareInstructions;
using FluentValidation;

namespace Backend.Veteriner.Application.CareInstructions.Commands.Update;

public sealed class UpdateCareInstructionCommandValidator : AbstractValidator<UpdateCareInstructionCommand>
{
    public UpdateCareInstructionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Text).NotEmpty().MaximumLength(CareInstruction.MaxTextLength);
    }
}
