using Backend.Veteriner.Application.Common.Clinic;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Common.ClinicRules;

public sealed class ReminderDispatchLogsClinicContextRulesTests
{
    [Theory]
    [InlineData("GET", "/api/v1/reminders/logs", true)]
    [InlineData("get", "/api/v1.0/reminders/logs", true)]
    [InlineData("GET", "/api/v1/reminders/logs/", true)]
    [InlineData("GET", "/api/v1/reminders/settings", false)]
    [InlineData("POST", "/api/v1/reminders/logs", false)]
    [InlineData("PUT", "/api/v1/reminders/logs", false)]
    public void ShouldIgnoreQueryClinicIdForResolver_Should_Match_Expected(string method, string path, bool expected)
    {
        ReminderDispatchLogsClinicContextRules.ShouldIgnoreQueryClinicIdForResolver(method, path)
            .Should().Be(expected);
    }

    [Fact]
    public void ShouldIgnoreQueryClinicIdForResolver_Should_BeFalse_When_PathNull()
    {
        ReminderDispatchLogsClinicContextRules.ShouldIgnoreQueryClinicIdForResolver("GET", null)
            .Should().BeFalse();
    }
}
