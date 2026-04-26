using Backend.Veteriner.Application.Appointments.Queries.GetCalendar;
using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Queries.GetCalendar.Validators;

public sealed class GetAppointmentsCalendarQueryValidator : AbstractValidator<GetAppointmentsCalendarQuery>
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(45);

    public GetAppointmentsCalendarQueryValidator()
    {
        RuleFor(x => x.DateFromUtc)
            .NotNull()
            .WithMessage("dateFromUtc zorunludur.");

        RuleFor(x => x.DateToUtc)
            .NotNull()
            .WithMessage("dateToUtc zorunludur.");

        RuleFor(x => x)
            .Must(q => !q.DateFromUtc.HasValue || !q.DateToUtc.HasValue || q.DateToUtc.Value > q.DateFromUtc.Value)
            .WithMessage("dateToUtc, dateFromUtc'den büyük olmalıdır.");

        RuleFor(x => x)
            .Must(q => !q.DateFromUtc.HasValue || !q.DateToUtc.HasValue || (q.DateToUtc.Value - q.DateFromUtc.Value) <= MaxWindow)
            .WithMessage("Tarih aralığı en fazla 45 gün olabilir.");
    }
}
