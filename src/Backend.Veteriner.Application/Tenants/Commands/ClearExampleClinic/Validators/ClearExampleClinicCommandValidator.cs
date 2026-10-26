using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.ClearExampleClinic.Validators;

public sealed class ClearExampleClinicCommandValidator : AbstractValidator<ClearExampleClinicCommand>
{
    public ClearExampleClinicCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
