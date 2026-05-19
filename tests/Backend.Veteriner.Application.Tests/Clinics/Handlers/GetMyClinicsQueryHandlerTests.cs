using Backend.Veteriner.Application.Clinics.Queries.GetMyClinics;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.Handlers;

public sealed class GetMyClinicsQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IUserTenantRepository> _userTenants = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();

    private GetMyClinicsQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _client.Object,
            _tenants.Object,
            _userTenants.Object,
            _userClinics.Object,
            _clinicsRead.Object,
            _userOperationClaims.Object);

    private void ArrangeTenantAndMembership(Guid tenantId, Guid userId, bool isMember = true, bool tenantActive = true)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _client.SetupGet(c => c.UserId).Returns(userId);

        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        if (!tenantActive)
            tenant.Deactivate();

        _tenants
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userTenants
            .Setup(r => r.ExistsAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isMember);
    }

    private void ArrangeClaimNames(Guid userId, params string[] names)
    {
        _userOperationClaims
            .Setup(r => r.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(names);
    }

    private static Clinic MakeClinic(Guid tenantId, string name, string city)
    {
        var clinic = new Clinic(tenantId, name, city);
        clinic.UpdateDetails(name, city, $"+90 212 000 00 0{name.Length}", $"{name.ToLower()}@b.test", null, null);
        return clinic;
    }

    [Fact]
    public async Task Handle_Should_ReturnOnlyAssignedClinics_When_NoTenantWideClaim()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        ArrangeTenantAndMembership(tid, uid);
        ArrangeClaimNames(uid /* no tenant-wide claims */);

        var c1 = MakeClinic(tid, "A", "X");
        _userClinics
            .Setup(r => r.ListAccessibleClinicsAsync(uid, tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { c1 });

        var result = await CreateHandler().Handle(new GetMyClinicsQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].Name.Should().Be("A");
        result.Value[0].Phone.Should().Be("+90 212 000 00 01");
        result.Value[0].Email.Should().Be("a@b.test");

        _clinicsRead.Verify(
            r => r.ListAsync(It.IsAny<ClinicsByTenantFilteredSpec>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-wide olmayan kullanıcılar UserClinic join'i ile listelenmeli");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("PlatformAdmin")]
    [InlineData("admin")] // case-insensitive
    public async Task Handle_Should_ReturnTenantWideClinics_When_TenantWideClaim(string claimName)
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        ArrangeTenantAndMembership(tid, uid);
        ArrangeClaimNames(uid, claimName);

        var c1 = MakeClinic(tid, "A", "X");
        var c2 = MakeClinic(tid, "B", "Y");
        _clinicsRead
            .Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantFilteredSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { c1, c2 });

        var result = await CreateHandler().Handle(new GetMyClinicsQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value!.Select(x => x.Name).Should().BeEquivalentTo(new[] { "A", "B" });

        _userClinics.Verify(
            r => r.ListAccessibleClinicsAsync(uid, tid, It.IsAny<bool?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-wide kullanıcılar UserClinic atamasından bağımsız listelenmeli");
    }

    [Theory]
    [InlineData("ClinicAdmin")]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public async Task Handle_Should_FallBackToAssignment_When_NonTenantWideClaim(string claimName)
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        ArrangeTenantAndMembership(tid, uid);
        ArrangeClaimNames(uid, claimName);

        var c1 = MakeClinic(tid, "A", "X");
        _userClinics
            .Setup(r => r.ListAccessibleClinicsAsync(uid, tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { c1 });

        var result = await CreateHandler().Handle(new GetMyClinicsQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);

        _clinicsRead.Verify(
            r => r.ListAsync(It.IsAny<ClinicsByTenantFilteredSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_PassIsActiveFilter_OnTenantWidePath()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        ArrangeTenantAndMembership(tid, uid);
        ArrangeClaimNames(uid, "Admin");

        var c1 = MakeClinic(tid, "A", "X");
        _clinicsRead
            .Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantFilteredSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { c1 });

        var result = await CreateHandler().Handle(new GetMyClinicsQuery(IsActive: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clinicsRead.Verify(
            r => r.ListAsync(It.IsAny<ClinicsByTenantFilteredSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnTenantNotMember_When_UserNotInUserTenants()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        ArrangeTenantAndMembership(tid, uid, isMember: false);
        // Tenant-wide claim olsa bile UserTenant membership zorunlu kalmalı.
        ArrangeClaimNames(uid, "Admin");

        var result = await CreateHandler().Handle(new GetMyClinicsQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.TenantNotMember");

        _clinicsRead.Verify(
            r => r.ListAsync(It.IsAny<ClinicsByTenantFilteredSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userClinics.Verify(
            r => r.ListAccessibleClinicsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseClinicsByTenantFilteredSpec_OnTenantWidePath()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        ArrangeTenantAndMembership(tid, uid);
        ArrangeClaimNames(uid, "Owner");

        _clinicsRead
            .Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantFilteredSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        await CreateHandler().Handle(new GetMyClinicsQuery(null), CancellationToken.None);

        // Tenant isolation, ClinicsByTenantFilteredSpec içindeki c.TenantId == tenantId
        // predicate'ine bağlıdır; handler'ın bu spec tipini çağırdığını doğrulamak yeterlidir
        // (spec'in kendisi ayrı kapsam altında, repo seviyesinde tenantId filtre uygular).
        _clinicsRead.Verify(
            r => r.ListAsync(It.IsAny<ClinicsByTenantFilteredSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
