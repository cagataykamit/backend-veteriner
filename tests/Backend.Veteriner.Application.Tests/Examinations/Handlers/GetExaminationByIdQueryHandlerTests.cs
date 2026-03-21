using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Queries.GetById;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class GetExaminationByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();

    private GetExaminationByIdQueryHandler CreateHandler()
        => new(_tenantContext.Object, _examinations.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetExaminationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _examinations.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Examination>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NoRowForTenant()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_Found()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var entity = new Examination(
            tid,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DateTime.UtcNow.AddHours(-1),
            "Şikayet",
            "Bulgu",
            null,
            null);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
        result.Value.TenantId.Should().Be(tid);
        result.Value.VisitReason.Should().Be("Şikayet");
    }
}
