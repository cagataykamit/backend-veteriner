using Backend.Veteriner.Application.Common;
using FluentValidation;

namespace Backend.Veteriner.Application.Imaging.Queries.GetList;

public sealed class GetImagingRecordsListQueryValidator : AbstractValidator<GetImagingRecordsListQuery>
{
    public GetImagingRecordsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);

        RuleFor(x => x.PageRequest.Sort)
            .Must(ImagingRecordListSort.Catalog.IsAllowedSort)
            .WithMessage(ImagingRecordListSort.Catalog.InvalidSortMessage);

        RuleFor(x => x.PageRequest.Order)
            .Must(ListSortOrder.IsAllowed)
            .WithMessage(ListSortOrder.InvalidMessage);

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("clinicId is invalid.");
        RuleFor(x => x.PetId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("petId is invalid.");
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("examinationId is invalid.");
        RuleFor(x => x.Modality).IsInEnum().When(x => x.Modality.HasValue);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);

        RuleFor(x => x)
            .Must(x => !x.DateFromUtc.HasValue || !x.DateToUtc.HasValue || x.DateFromUtc <= x.DateToUtc)
            .WithMessage("dateFromUtc, dateToUtc'den küçük veya eşit olmalıdır.");
    }
}
