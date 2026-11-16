using FluentValidation;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetCalendar.Validators;

public sealed class GetVaccinationsCalendarQueryValidator : AbstractValidator<GetVaccinationsCalendarQuery>
{
    public GetVaccinationsCalendarQueryValidator()
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
            .Must(q => !q.DateFromUtc.HasValue || !q.DateToUtc.HasValue
                       || (q.DateToUtc.Value - q.DateFromUtc.Value) <= GetVaccinationsCalendarQueryHandler.MaxWindow)
            .WithMessage("Tarih aralığı en fazla 45 gün olabilir.");

        RuleForEach(x => x.States)
            .IsInEnum()
            .WithMessage("Geçersiz takvim durumu.");
    }
}
