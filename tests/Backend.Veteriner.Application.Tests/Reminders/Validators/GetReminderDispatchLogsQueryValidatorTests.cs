using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reminders.Queries.GetLogs;
using Backend.Veteriner.Application.Reminders.Queries.GetLogs.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Reminders.Validators;

public sealed class GetReminderDispatchLogsQueryValidatorTests
{
    private readonly GetReminderDispatchLogsQueryValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_Page_IsLessThanOne()
    {
        var query = new GetReminderDispatchLogsQuery(new PageRequest { Page = 0, PageSize = 20 });
        var result = _validator.Validate(query);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_Should_Fail_When_DateRangeInvalid()
    {
        var query = new GetReminderDispatchLogsQuery(
            new PageRequest { Page = 1, PageSize = 20 },
            FromUtc: DateTime.UtcNow,
            ToUtc: DateTime.UtcNow.AddDays(-1));
        var result = _validator.Validate(query);
        result.IsValid.Should().BeFalse();
    }
}
