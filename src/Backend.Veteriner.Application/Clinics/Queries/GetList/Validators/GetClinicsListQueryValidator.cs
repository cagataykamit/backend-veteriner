using Backend.Veteriner.Application.Clinics.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Queries.GetList.Validators;

public sealed class GetClinicsListQueryValidator : AbstractValidator<GetClinicsListQuery>
{
    public GetClinicsListQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
