using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Commands.SetKvkkConsent;

public sealed class SetClientKvkkConsentCommandValidator : AbstractValidator<SetClientKvkkConsentCommand>
{
    public SetClientKvkkConsentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}
