using FluentValidation;

namespace Backend.Veteriner.Application.Payments.Commands.Void;

public sealed class VoidPaymentCommandValidator : AbstractValidator<VoidPaymentCommand>
{
    public VoidPaymentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
