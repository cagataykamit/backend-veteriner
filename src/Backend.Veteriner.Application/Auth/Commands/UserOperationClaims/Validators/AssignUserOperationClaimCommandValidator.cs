using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Assign;
using FluentValidation;

namespace Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Validators;

public sealed class AssignUserOperationClaimCommandValidator : AbstractValidator<AssignUserOperationClaimCommand>
{
    public AssignUserOperationClaimCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OperationClaimId).NotEmpty();
    }
}
