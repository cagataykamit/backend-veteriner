using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetExampleClinicStatus.Validators;

public sealed class GetExampleClinicStatusQueryValidator : AbstractValidator<GetExampleClinicStatusQuery>
{
    public GetExampleClinicStatusQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
