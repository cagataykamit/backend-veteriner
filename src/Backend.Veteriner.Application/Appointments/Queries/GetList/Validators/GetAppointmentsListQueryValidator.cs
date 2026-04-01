using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Common;
using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Queries.GetList.Validators;

public sealed class GetAppointmentsListQueryValidator : AbstractValidator<GetAppointmentsListQuery>
{
    public GetAppointmentsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);

        RuleFor(x => x)
            .Must(q =>
                !q.DateFromUtc.HasValue
                || !q.DateToUtc.HasValue
                || q.DateFromUtc.Value <= q.DateToUtc.Value)
            .WithMessage("dateFromUtc, dateToUtc'den büyük olamaz.");
    }
}
