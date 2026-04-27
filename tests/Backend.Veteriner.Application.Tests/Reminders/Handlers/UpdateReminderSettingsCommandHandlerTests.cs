using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reminders.Commands.UpdateSettings;
using Backend.Veteriner.Domain.Reminders;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reminders.Handlers;

public sealed class UpdateReminderSettingsCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IReadRepository<TenantReminderSettings>> _settingsRead = new();
    private readonly Mock<IRepository<TenantReminderSettings>> _settingsWrite = new();

    private UpdateReminderSettingsCommandHandler CreateHandler()
        => new(_tenant.Object, _settingsRead.Object, _settingsWrite.Object);

    [Fact]
    public async Task Handle_Should_Create_When_RecordMissing()
    {
        _tenant.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        _settingsRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantReminderSettings>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantReminderSettings?)null);

        var cmd = new UpdateReminderSettingsCommand(true, 48, true, 7, true);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _settingsWrite.Verify(x => x.AddAsync(It.IsAny<TenantReminderSettings>(), It.IsAny<CancellationToken>()), Times.Once);
        _settingsWrite.Verify(x => x.UpdateAsync(It.IsAny<TenantReminderSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        _settingsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Update_When_RecordExists()
    {
        _tenant.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        var existing = TenantReminderSettings.CreateDefault(Guid.NewGuid());
        _settingsRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantReminderSettings>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var cmd = new UpdateReminderSettingsCommand(true, 12, false, 3, false);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _settingsWrite.Verify(x => x.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _settingsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
