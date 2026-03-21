using Backend.Veteriner.Application.Users.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Users.Commands.Create.Validators;

public sealed class AdminCreateUserCommandValidator : AbstractValidator<AdminCreateUserCommand>
{
    public AdminCreateUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(200);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);
    }
}
