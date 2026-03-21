using Backend.Veteriner.Application.Appointments.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Queries.GetList.Validators;

public sealed class GetAppointmentsListQueryValidator : AbstractValidator<GetAppointmentsListQuery>
{
    public GetAppointmentsListQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x)
            .Must(q =>
                !q.DateFromUtc.HasValue
                || !q.DateToUtc.HasValue
                || q.DateFromUtc.Value <= q.DateToUtc.Value)
            .WithMessage("dateFromUtc, dateToUtc'den büyük olamaz.");
    }
}
