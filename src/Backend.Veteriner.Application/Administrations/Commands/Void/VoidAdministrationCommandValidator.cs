using FluentValidation;

namespace Backend.Veteriner.Application.Administrations.Commands.Void;

public sealed class VoidAdministrationCommandValidator : AbstractValidator<VoidAdministrationCommand>
{
    public VoidAdministrationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
