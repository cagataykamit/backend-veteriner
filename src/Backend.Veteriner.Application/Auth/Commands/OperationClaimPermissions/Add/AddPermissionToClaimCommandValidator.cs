using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Add
{
    public sealed class AddPermissionToClaimCommandValidator : AbstractValidator<AddPermissionToClaimCommand>
    {
        public AddPermissionToClaimCommandValidator()
        {
            RuleFor(x => x.OperationClaimId).NotEmpty();
            RuleFor(x => x.PermissionId).NotEmpty();
        }
    }
}
