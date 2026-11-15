using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Queries.GetList.Validators;

public sealed class GetExaminationsListQueryValidator : AbstractValidator<GetExaminationsListQuery>
{
    public GetExaminationsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Klinik filtresi geçersiz.");

        RuleFor(x => x.PetId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Hasta filtresi geçersiz.");

        RuleFor(x => x.AppointmentId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Randevu filtresi geçersiz.");

        RuleFor(x => x.VisitId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geliş filtresi geçersiz.");

        RuleFor(x => x.ClientId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Müşteri filtresi geçersiz.");

        RuleFor(x => x.PageRequest.Sort)
            .Must(ExaminationListSort.Catalog.IsAllowedSort)
            .WithMessage(ExaminationListSort.Catalog.InvalidSortMessage);

        RuleFor(x => x.PageRequest.Order)
            .Must(ListSortOrder.IsAllowed)
            .WithMessage(ListSortOrder.InvalidMessage);

        RuleFor(x => x)
            .Must(x => !x.ExaminedOnLocalDate.HasValue || (!x.DateFromUtc.HasValue && !x.DateToUtc.HasValue))
            .WithMessage("examinedOnLocalDate ile dateFromUtc/dateToUtc birlikte kullanılamaz.");

        RuleFor(x => x)
            .Must(x => !x.DateFromUtc.HasValue || !x.DateToUtc.HasValue || x.DateFromUtc < x.DateToUtc)
            .WithMessage("dateFromUtc, dateToUtc'den küçük olmalıdır (dateToUtc üst sınırı hariç).");
    }
}
