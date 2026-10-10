using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tests.TestHelpers;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

public sealed class ClinicVeterinariansTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicReadScopeResolver> _scope = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IClinicVeterinarianReader> _reader = new();

    public ClinicVeterinariansTests() => _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);

    private GetClinicVeterinariansQueryHandler CreateHandler() => new(_tenant.Object, _scope.Object, _reader.Object);

    [Fact]
    public async Task Handle_Should_Return_Veterinarians_From_Reader()
    {
        var list = new List<ClinicVeterinarianDto> { new(Guid.NewGuid(), "Ali Veli") };
        _reader.Setup(r => r.ListAsync(_tenantId, _clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(list);

        var result = await CreateHandler().Handle(new GetClinicVeterinariansQuery(_clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(list);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Clinic_Not_Assigned_And_Not_Query_Reader()
    {
        _scope.SetupAccessDenied();

        var result = await CreateHandler().Handle(new GetClinicVeterinariansQuery(_clinicId), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _reader.Verify(r => r.ListAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Tenant_Missing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetClinicVeterinariansQuery(_clinicId), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }
}
