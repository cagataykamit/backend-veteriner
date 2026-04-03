using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Hospitalizations.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Queries.GetList.Validators;

public sealed class GetHospitalizationsListQueryValidator : AbstractValidator<GetHospitalizationsListQuery>
{
    public GetHospitalizationsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("clinicId is invalid.");

        RuleFor(x => x.PetId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("petId is invalid.");

        RuleFor(x => x)
            .Must(x => !x.DateFromUtc.HasValue || !x.DateToUtc.HasValue || x.DateFromUtc <= x.DateToUtc)
            .WithMessage("dateFromUtc, dateToUtc'den küçük veya eşit olmalıdır.");
    }
}
