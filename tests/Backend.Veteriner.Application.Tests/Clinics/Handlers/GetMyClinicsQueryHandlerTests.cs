using Backend.Veteriner.Application.Clinics.Queries.GetMyClinics;
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

    private GetMyClinicsQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _client.Object,
            _tenants.Object,
            _userTenants.Object,
            _userClinics.Object);

    [Fact]
    public async Task Handle_Should_ReturnOnlyAssignedClinics()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        var c1 = new Clinic(tid, "A", "X");
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _client.SetupGet(c => c.UserId).Returns(uid);
        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userTenants.Setup(r => r.ExistsAsync(uid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _userClinics
            .Setup(r => r.ListAccessibleClinicsAsync(uid, tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { c1 });

        var result = await CreateHandler().Handle(new GetMyClinicsQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].Name.Should().Be("A");
    }
}
