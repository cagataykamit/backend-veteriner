using Backend.Veteriner.Application.Reminders.Queries.GetLogs;
using FluentValidation;

namespace Backend.Veteriner.Application.Reminders.Queries.GetLogs.Validators;

public sealed class GetReminderDispatchLogsQueryValidator : AbstractValidator<GetReminderDispatchLogsQuery>
{
    public GetReminderDispatchLogsQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 100);

        RuleFor(x => x)
            .Must(q => !q.FromUtc.HasValue || !q.ToUtc.HasValue || q.FromUtc.Value <= q.ToUtc.Value)
            .WithMessage("fromUtc, toUtc'den büyük olamaz.");
    }
}
