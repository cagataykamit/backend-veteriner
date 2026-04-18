using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.AssignMemberClinic.Validators;

public sealed class AssignTenantMemberClinicCommandValidator : AbstractValidator<AssignTenantMemberClinicCommand>
{
    public AssignTenantMemberClinicCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.ClinicId).NotEmpty();
    }
}
