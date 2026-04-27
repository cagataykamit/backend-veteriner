using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Application.Reminders.Queries.GetLogs;
using Backend.Veteriner.Domain.Reminders;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reminders.Handlers;

public sealed class GetReminderDispatchLogsQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IReadRepository<ReminderDispatchLog>> _logs = new();

    private GetReminderDispatchLogsQueryHandler CreateHandler()
        => new(_tenant.Object, _logs.Object);

    [Fact]
    public async Task Handle_Should_ReturnEmptyPaged_When_NoRows()
    {
        _tenant.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        _logs.Setup(x => x.ListAsync(It.IsAny<ISpecification<ReminderDispatchLog, ReminderDispatchLogItemDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReminderDispatchLogItemDto>());
        _logs.Setup(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(
            new GetReminderDispatchLogsQuery(new PageRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        result.Value.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Should_ReturnPagedRows()
    {
        _tenant.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        _logs.Setup(x => x.ListAsync(It.IsAny<ISpecification<ReminderDispatchLog, ReminderDispatchLogItemDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReminderDispatchLogItemDto>
            {
                new(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    ReminderType.Appointment,
                    ReminderSourceEntityType.Appointment,
                    Guid.NewGuid(),
                    "test@clinic.local",
                    "Ali",
                    DateTime.UtcNow.AddHours(2),
                    DateTime.UtcNow.AddHours(4),
                    ReminderDispatchStatus.Pending,
                    null,
                    null,
                    null,
                    null,
                    DateTime.UtcNow)
            });
        _logs.Setup(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new GetReminderDispatchLogsQuery(
                new PageRequest { Page = 1, PageSize = 20 },
                ReminderType.Appointment,
                ReminderDispatchStatus.Pending,
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);
        result.Value.TotalItems.Should().Be(1);
    }
}
