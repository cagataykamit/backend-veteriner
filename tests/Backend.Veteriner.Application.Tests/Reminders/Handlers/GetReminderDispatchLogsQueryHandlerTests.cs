using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Application.Reminders.Queries.GetLogs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Reminders;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reminders.Handlers;

public sealed class GetReminderDispatchLogsQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _guard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<ReminderDispatchLog>> _logs = new();

    private GetReminderDispatchLogsQueryHandler CreateHandler()
        => new(_tenant.Object, _client.Object, _guard.Object, _userClinics.Object, _clinics.Object, _logs.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_UserContextMissing()
    {
        var tenantId = Guid.NewGuid();
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _client.SetupGet(x => x.UserId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            new GetReminderDispatchLogsQuery(new PageRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnEmptyPaged_When_NoRows()
    {
        var tenantId = Guid.NewGuid();
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _client.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _guard.Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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
    public async Task Handle_Should_ReturnPagedRows_When_Admin_NoClinicFilter()
    {
        var tenantId = Guid.NewGuid();
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _client.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _guard.Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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
        _clinics.Verify(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_Admin_ClinicId_NotInTenant()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _client.SetupGet(x => x.UserId).Returns(userId);
        _guard.Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var unknownClinicId = Guid.NewGuid();
        var result = await CreateHandler().Handle(
            new GetReminderDispatchLogsQuery(new PageRequest { Page = 1, PageSize = 20 }, ClinicId: unknownClinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Clinics.NotFound");
        _logs.Verify(x => x.ListAsync(It.IsAny<ISpecification<ReminderDispatchLog, ReminderDispatchLogItemDto>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnAccessDenied_When_ClinicAdmin_Not_Assigned_To_Clinic()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assignedClinic = new Clinic(tenantId, "A", "Istanbul");

        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _client.SetupGet(x => x.UserId).Returns(userId);
        _guard.Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userClinics.Setup(x => x.ListAccessibleClinicsAsync(userId, tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { assignedClinic });

        var otherClinicId = Guid.NewGuid();
        while (otherClinicId == assignedClinic.Id)
            otherClinicId = Guid.NewGuid();

        var result = await CreateHandler().Handle(
            new GetReminderDispatchLogsQuery(new PageRequest { Page = 1, PageSize = 20 }, ClinicId: otherClinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Clinics.AccessDenied");
        _logs.Verify(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_LoadLogs_When_ClinicAdmin_Filter_Assigned_Clinic()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assignedClinic = new Clinic(tenantId, "A", "Istanbul");

        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _client.SetupGet(x => x.UserId).Returns(userId);
        _guard.Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userClinics.Setup(x => x.ListAccessibleClinicsAsync(userId, tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { assignedClinic });

        _logs.Setup(x => x.ListAsync(It.IsAny<ISpecification<ReminderDispatchLog, ReminderDispatchLogItemDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReminderDispatchLogItemDto>());
        _logs.Setup(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(
            new GetReminderDispatchLogsQuery(new PageRequest { Page = 1, PageSize = 20 }, ClinicId: assignedClinic.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        _logs.Verify(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnEmpty_When_ClinicAdmin_Has_No_Assigned_Clinics()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _client.SetupGet(x => x.UserId).Returns(userId);
        _guard.Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userClinics.Setup(x => x.ListAccessibleClinicsAsync(userId, tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        _logs.Setup(x => x.ListAsync(It.IsAny<ISpecification<ReminderDispatchLog, ReminderDispatchLogItemDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReminderDispatchLogItemDto>());
        _logs.Setup(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(
            new GetReminderDispatchLogsQuery(new PageRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        _logs.Verify(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Match_Count_And_List_When_ClinicAdmin_MultiClinic_Scope()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var c1 = new Clinic(tenantId, "A", "Istanbul");
        var c2 = new Clinic(tenantId, "B", "Ankara");

        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _client.SetupGet(x => x.UserId).Returns(userId);
        _guard.Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userClinics.Setup(x => x.ListAccessibleClinicsAsync(userId, tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { c1, c2 });

        _logs.Setup(x => x.ListAsync(It.IsAny<ISpecification<ReminderDispatchLog, ReminderDispatchLogItemDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReminderDispatchLogItemDto>());
        _logs.Setup(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await CreateHandler().Handle(
            new GetReminderDispatchLogsQuery(new PageRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(5);
        _logs.Verify(x => x.ListAsync(It.IsAny<ISpecification<ReminderDispatchLog, ReminderDispatchLogItemDto>>(), It.IsAny<CancellationToken>()), Times.Once);
        _logs.Verify(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Load_When_Admin_Valid_ClinicId()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clinic = new Clinic(tenantId, "A", "Istanbul");

        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _client.SetupGet(x => x.UserId).Returns(userId);
        _guard.Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        _logs.Setup(x => x.ListAsync(It.IsAny<ISpecification<ReminderDispatchLog, ReminderDispatchLogItemDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReminderDispatchLogItemDto>());
        _logs.Setup(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(
            new GetReminderDispatchLogsQuery(new PageRequest { Page = 1, PageSize = 20 }, ClinicId: clinic.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clinics.Verify(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()), Times.Once);
        _logs.Verify(x => x.CountAsync(It.IsAny<ISpecification<ReminderDispatchLog>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
