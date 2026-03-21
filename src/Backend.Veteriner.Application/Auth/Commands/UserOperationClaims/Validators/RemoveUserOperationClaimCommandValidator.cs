using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Remove;
using FluentValidation;

namespace Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Validators;

public sealed class RemoveUserOperationClaimCommandValidator : AbstractValidator<RemoveUserOperationClaimCommand>
{
    public RemoveUserOperationClaimCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OperationClaimId).NotEmpty();
    }
}
