using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.CreateInvite.Validators;

public sealed class CreateTenantInviteCommandValidator : AbstractValidator<CreateTenantInviteCommand>
{
    public CreateTenantInviteCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.OperationClaimId).NotEmpty();
    }
}
