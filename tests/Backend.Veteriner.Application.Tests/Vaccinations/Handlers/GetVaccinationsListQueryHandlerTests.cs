using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Vaccinations.Queries.GetList;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Handlers;

public sealed class GetVaccinationsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();

    private GetVaccinationsListQueryHandler CreateHandler()
        => new(_tenantContext.Object, _vaccinations.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetVaccinationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _vaccinations.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<Vaccination>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseTenantScopedSpecs_When_ContextPresent()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _vaccinations.Setup(r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<VaccinationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination>());
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetVaccinationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        _vaccinations.Verify(
            r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _vaccinations.Verify(
            r => r.ListAsync(It.IsAny<VaccinationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
