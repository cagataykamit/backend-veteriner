using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class GetExaminationsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();

    private GetExaminationsListQueryHandler CreateHandler()
        => new(_tenantContext.Object, _examinations.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetExaminationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _examinations.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<Examination>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_QueryWithTenantScopedSpecs_When_ContextPresent()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Examination>());
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetExaminationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        result.Value.TotalItems.Should().Be(0);
        _examinations.Verify(
            r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _examinations.Verify(
            r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
