using Backend.Veteriner.Application.Clinics.Queries.GetList;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.Queries;

public sealed class GetClinicsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();

    public GetClinicsListQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private GetClinicsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _assignmentGuard.Object,
            _userClinics.Object,
            _clinicsRead.Object);

    private static Clinic BuildClinic(Guid id, Guid tenantId, string name)
    {
        var clinic = new Clinic(tenantId, name, "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, id);
        return clinic;
    }

    [Fact]
    public async Task Should_UseTenantPagedSpec_When_ScopeOff()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        var rows = new List<Clinic> { BuildClinic(Guid.NewGuid(), tenantId, "A") };
        _clinicsRead.Setup(x => x.CountAsync(It.IsAny<ClinicsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _clinicsRead.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var result = await CreateHandler().Handle(
            new GetClinicsListQuery(new PageRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().HaveCount(1);
        _userClinics.Verify(
            x => x.ListAccessibleClinicsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ReturnOnlyAssignedClinics_When_ClinicAdminScope()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assignedId = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(userId);
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var assigned = new[]
        {
            BuildClinic(assignedId, tenantId, "Şube 1"),
            BuildClinic(Guid.NewGuid(), tenantId, "Şube 2"),
        };
        _userClinics
            .Setup(x => x.ListAccessibleClinicsAsync(userId, tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned);

        var result = await CreateHandler().Handle(
            new GetClinicsListQuery(new PageRequest { Page = 1, PageSize = 10 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Select(i => i.Id).Should().BeEquivalentTo(assigned.Select(c => c.Id));
        _clinicsRead.Verify(x => x.CountAsync(It.IsAny<ClinicsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _clinicsRead.Verify(x => x.ListAsync(It.IsAny<ClinicsByTenantPagedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_Page_AssignedClinicList_InMemory()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(userId);
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var assigned = Enumerable.Range(0, 5)
            .Select(i => BuildClinic(Guid.NewGuid(), tenantId, $"Klinik {i}"))
            .OrderBy(c => c.Name)
            .ToList();
        _userClinics
            .Setup(x => x.ListAccessibleClinicsAsync(userId, tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned);

        var result = await CreateHandler().Handle(
            new GetClinicsListQuery(new PageRequest { Page = 2, PageSize = 2 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(5);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Page.Should().Be(2);
    }
}
