using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Backend.Veteriner.Application.Auth.Commands.Permissions.Create
{
    public sealed class CreatePermissionCommandValidator : AbstractValidator<CreatePermissionCommand>
    {
        public CreatePermissionCommandValidator()
        {
            RuleFor(x => x.Code)
                .NotEmpty().MaximumLength(128)
                .Matches(@"^[A-Za-z0-9\.\-:]+$").WithMessage("Code sadece harf/rakam . - : i�erebilir.");
            RuleFor(x => x.Description).MaximumLength(512);
        }
    }
}
