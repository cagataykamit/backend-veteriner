using Backend.Veteriner.Application.Payments.ReadModels;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Payments.ReadModels;

public sealed class PaymentReadModelParityEvaluatorTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime PaidAt = new(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);

    private static PaymentReadModelParityEvaluator.RowSnapshot Row(
        Guid paymentId,
        decimal amount = 100m,
        string currency = "TRY",
        int method = 0,
        DateTime? paidAt = null,
        string clientName = "Ada Lovelace",
        string? petName = null,
        string? notes = null)
        => new(
            paymentId,
            ClientId,
            null,
            amount,
            currency,
            method,
            paidAt ?? PaidAt,
            clientName,
            petName,
            notes);

    [Fact]
    public void Evaluate_Should_BeInSync_WhenCountsRowsAndOrderingMatch()
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var command = new[] { Row(p2, paidAt: PaidAt.AddHours(1)), Row(p1) };
        var query = new[] { Row(p2, paidAt: PaidAt.AddHours(1)), Row(p1) };

        var result = PaymentReadModelParityEvaluator.Evaluate(2, 2, command, query, TenantId, ClinicId);

        result.InSync.Should().BeTrue();
        result.CountInSync.Should().BeTrue();
        result.RowSampleParityInSync.Should().BeTrue();
        result.RecentOrderingInSync.Should().BeTrue();
        result.RowSampleMismatchCount.Should().Be(0);
    }

    [Fact]
    public void Evaluate_Should_ReportCountMismatch_WhenCountsDiffer()
    {
        var result = PaymentReadModelParityEvaluator.Evaluate(3, 1, [], [], TenantId, ClinicId);

        result.CountInSync.Should().BeFalse();
        result.CountDifference.Should().Be(2);
        result.InSync.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Should_ReportRowFieldMismatch_WhenCriticalFieldDiffers()
    {
        var paymentId = Guid.NewGuid();
        var command = new[] { Row(paymentId, amount: 250m) };
        var query = new[] { Row(paymentId, amount: 200m) };

        var result = PaymentReadModelParityEvaluator.Evaluate(1, 1, command, query, TenantId, ClinicId);

        result.CountInSync.Should().BeTrue();
        result.RowSampleParityInSync.Should().BeFalse();
        result.RowSampleMismatchCount.Should().Be(1);
        result.RowSampleMismatches[0].PaymentId.Should().Be(paymentId);
        result.RowSampleMismatches[0].Field.Should().Be("Amount");
        result.InSync.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Should_ReportRowMismatch_WhenRowMissingInQuery()
    {
        var paymentId = Guid.NewGuid();
        var command = new[] { Row(paymentId) };

        var result = PaymentReadModelParityEvaluator.Evaluate(
            1, 0, command, [], TenantId, ClinicId);

        result.RowSampleParityInSync.Should().BeFalse();
        result.RowSampleMismatches.Should().Contain(m => m.PaymentId == paymentId && m.Field == "missing-in-query");
    }

    [Fact]
    public void Evaluate_Should_ReportOrderingMismatch_WhenRecentSequenceDiffers()
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var command = new[] { Row(p1, paidAt: PaidAt.AddHours(2)), Row(p2, paidAt: PaidAt) };
        var query = new[] { Row(p2, paidAt: PaidAt), Row(p1, paidAt: PaidAt.AddHours(2)) };

        var result = PaymentReadModelParityEvaluator.Evaluate(2, 2, command, query, TenantId, ClinicId);

        result.CountInSync.Should().BeTrue();
        result.RecentOrderingInSync.Should().BeFalse();
        result.InSync.Should().BeFalse();
    }
}
