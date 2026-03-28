using FluentValidation;

namespace Backend.Veteriner.Application.Payments.Commands.Update;

public sealed class UpdatePaymentCommandValidator : AbstractValidator<UpdatePaymentCommand>
{
    public UpdatePaymentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ClientId).NotEmpty();

        RuleFor(x => x.Amount).GreaterThan(0);

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(c => c.Trim().Length == 3 && c.Trim().All(char.IsLetter))
            .WithMessage("Para birimi 3 harfli ISO 4217 kodu olmalıdır (örn. TRY).");

        RuleFor(x => x.Method).IsInEnum();

        RuleFor(x => x.PaidAtUtc).NotEqual(default(DateTime));

        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
