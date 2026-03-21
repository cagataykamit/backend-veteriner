using FluentValidation;

namespace Backend.Veteriner.Application.Users.Commands.Claims.Remove.Validators;

public sealed class AdminRemoveUserClaimCommandValidator : AbstractValidator<AdminRemoveUserClaimCommand>
{
    public AdminRemoveUserClaimCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OperationClaimId).NotEmpty();
    }
}
