using Backend.Veteriner.Application.Clients.ReadModels;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Clients.ReadModels;

public sealed class ClientReadModelParityEvaluatorTests
{
    [Fact]
    public void Evaluate_Should_BeInSync_WhenCountsEqual()
    {
        var result = ClientReadModelParityEvaluator.Evaluate(commandCount: 10, queryCount: 10);

        result.InSync.Should().BeTrue();
        result.Difference.Should().Be(0);
        result.AbsoluteDifference.Should().Be(0);
        result.ScopeTenantId.Should().BeNull();
    }

    [Fact]
    public void Evaluate_Should_ReportPositiveDifference_WhenReadModelBehind()
    {
        var result = ClientReadModelParityEvaluator.Evaluate(commandCount: 12, queryCount: 9);

        result.InSync.Should().BeFalse();
        result.Difference.Should().Be(3);
        result.AbsoluteDifference.Should().Be(3);
    }

    [Fact]
    public void Evaluate_Should_ReportNegativeDifference_WhenReadModelAhead()
    {
        var result = ClientReadModelParityEvaluator.Evaluate(commandCount: 5, queryCount: 8);

        result.InSync.Should().BeFalse();
        result.Difference.Should().Be(-3);
        result.AbsoluteDifference.Should().Be(3);
    }

    [Fact]
    public void Evaluate_Should_CarryScopeTenantId()
    {
        var tenantId = Guid.NewGuid();

        var result = ClientReadModelParityEvaluator.Evaluate(commandCount: 1, queryCount: 1, tenantId);

        result.ScopeTenantId.Should().Be(tenantId);
        result.InSync.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Should_BeInSync_WhenBothEmpty()
    {
        var result = ClientReadModelParityEvaluator.Evaluate(commandCount: 0, queryCount: 0);

        result.InSync.Should().BeTrue();
    }
}
