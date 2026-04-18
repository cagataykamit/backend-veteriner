using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.RemoveMemberClinic.Validators;

public sealed class RemoveTenantMemberClinicCommandValidator : AbstractValidator<RemoveTenantMemberClinicCommand>
{
    public RemoveTenantMemberClinicCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.ClinicId).NotEmpty();
    }
}
