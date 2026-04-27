using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reminders.Queries.GetSettings;
using Backend.Veteriner.Domain.Reminders;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reminders.Handlers;

public sealed class GetReminderSettingsQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IReadRepository<TenantReminderSettings>> _settingsRead = new();

    private GetReminderSettingsQueryHandler CreateHandler()
        => new(_tenant.Object, _settingsRead.Object);

    [Fact]
    public async Task Handle_Should_ReturnDefault_When_RecordMissing()
    {
        var tenantId = Guid.NewGuid();
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _settingsRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantReminderSettings>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantReminderSettings?)null);

        var result = await CreateHandler().Handle(new GetReminderSettingsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AppointmentRemindersEnabled.Should().BeFalse();
        result.Value.AppointmentReminderHoursBefore.Should().Be(24);
        result.Value.VaccinationRemindersEnabled.Should().BeFalse();
        result.Value.VaccinationReminderDaysBefore.Should().Be(3);
        result.Value.EmailChannelEnabled.Should().BeTrue();
    }
}
