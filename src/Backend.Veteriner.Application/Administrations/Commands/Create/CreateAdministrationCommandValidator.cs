using FluentValidation;

namespace Backend.Veteriner.Application.Administrations.Commands.Create;

public sealed class CreateAdministrationCommandValidator : AbstractValidator<CreateAdministrationCommand>
{
    public CreateAdministrationCommandValidator()
    {
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
    }
}
