using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Vaccinations.Queries.GetById;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Handlers;

public sealed class GetVaccinationByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();

    private GetVaccinationByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object, _vaccinations.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetVaccinationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _vaccinations.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Vaccination>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NoRowForTenant()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _vaccinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vaccination?)null);

        var result = await CreateHandler().Handle(new GetVaccinationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_Found()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var entity = new Vaccination(
            tid,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Kuduz",
            VaccinationStatus.Applied,
            DateTime.UtcNow.AddHours(-1),
            null,
            null);
        _vaccinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await CreateHandler().Handle(new GetVaccinationByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.VaccineName.Should().Be("Kuduz");
        result.Value.Status.Should().Be(VaccinationStatus.Applied);
    }
}
