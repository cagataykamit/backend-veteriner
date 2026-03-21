using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Queries.GetById;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments.Handlers;

public sealed class GetPaymentByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();

    private GetPaymentByIdQueryHandler CreateHandler()
        => new(_tenantContext.Object, _payments.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _payments.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Payment>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NoRowForTenant()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _payments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_Found()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var entity = new Payment(
            tid,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            10m,
            "TRY",
            PaymentMethod.Card,
            DateTime.UtcNow.AddMinutes(-30),
            null);
        _payments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Amount.Should().Be(10m);
        result.Value.Method.Should().Be(PaymentMethod.Card);
    }
}
