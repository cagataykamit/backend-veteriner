using Backend.Veteriner.Application.Tenants.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.Create.Validators;

public sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(300);
    }
}
