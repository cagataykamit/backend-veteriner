using Backend.Veteriner.Application.Clients.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Commands.Create.Validators;

public sealed class CreateClientCommandValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(300);
        // Telefon opsiyonel: resepsiyonda önce ad kaydı, telefon sonra eklenebilir.
        RuleFor(x => x.Phone)
            .MaximumLength(50)
            .Matches(@"^[\d\s\+\-\(\)]*$")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Telefon yalnızca rakam ve + - ( ) boşluk içerebilir.");
    }
}
