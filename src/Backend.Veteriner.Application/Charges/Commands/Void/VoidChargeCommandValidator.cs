using FluentValidation;

namespace Backend.Veteriner.Application.Charges.Commands.Void;

public sealed class VoidChargeCommandValidator : AbstractValidator<VoidChargeCommand>
{
    public VoidChargeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
