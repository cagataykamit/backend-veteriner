using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Remove
{
    public sealed class RemovePermissionFromClaimCommandValidator : AbstractValidator<RemovePermissionFromClaimCommand>
    {
        public RemovePermissionFromClaimCommandValidator()
        {
            RuleFor(x => x.OperationClaimId).NotEmpty();
            RuleFor(x => x.PermissionId).NotEmpty();
        }
    }
}
