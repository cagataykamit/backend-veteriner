using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.LoadExampleClinic.Validators;

public sealed class LoadExampleClinicCommandValidator : AbstractValidator<LoadExampleClinicCommand>
{
    public LoadExampleClinicCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
