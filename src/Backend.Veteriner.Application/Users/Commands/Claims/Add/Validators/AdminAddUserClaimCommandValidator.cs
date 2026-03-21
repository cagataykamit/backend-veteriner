using FluentValidation;

namespace Backend.Veteriner.Application.Users.Commands.Claims.Add.Validators;

public sealed class AdminAddUserClaimCommandValidator : AbstractValidator<AdminAddUserClaimCommand>
{
    public AdminAddUserClaimCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OperationClaimId).NotEmpty();
    }
}
