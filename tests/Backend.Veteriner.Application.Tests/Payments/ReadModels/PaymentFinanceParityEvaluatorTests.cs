using Backend.Veteriner.Application.Payments.ReadModels;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Payments.ReadModels;

public sealed class PaymentFinanceParityEvaluatorTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClinicB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly Day = new(2026, 6, 19);

    [Fact]
    public void Evaluate_Should_BeInSync_WhenCountsAndBucketsMatch()
    {
        var bucket = new PaymentFinanceParityEvaluator.DailyBucketSnapshot(
            TenantId, ClinicA, Day, "TRY", 300m, 2);

        var result = PaymentFinanceParityEvaluator.Evaluate(
            commandPaymentCount: 2,
            queryContributionCount: 2,
            commandBuckets: [bucket],
            queryBuckets: [bucket],
            TenantId);

        result.InSync.Should().BeTrue();
        result.CountInSync.Should().BeTrue();
        result.DailyBucketParityInSync.Should().BeTrue();
        result.DailyBucketMismatchCount.Should().Be(0);
    }

    [Fact]
    public void Evaluate_Should_ReportCountBehind_WhenContributionsMissing()
    {
        var result = PaymentFinanceParityEvaluator.Evaluate(
            commandPaymentCount: 3,
            queryContributionCount: 1,
            commandBuckets: [],
            queryBuckets: [],
            TenantId);

        result.CountInSync.Should().BeFalse();
        result.CountDifference.Should().Be(2);
        result.InSync.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Should_ReportDailyBucketMismatch_WhenTotalsDiffer()
    {
        var commandBucket = new PaymentFinanceParityEvaluator.DailyBucketSnapshot(
            TenantId, ClinicA, Day, "TRY", 250m, 1);
        var queryBucket = new PaymentFinanceParityEvaluator.DailyBucketSnapshot(
            TenantId, ClinicA, Day, "TRY", 200m, 1);

        var result = PaymentFinanceParityEvaluator.Evaluate(
            commandPaymentCount: 1,
            queryContributionCount: 1,
            commandBuckets: [commandBucket],
            queryBuckets: [queryBucket],
            TenantId);

        result.CountInSync.Should().BeTrue();
        result.DailyBucketParityInSync.Should().BeFalse();
        result.DailyBucketMismatchCount.Should().Be(1);
        result.DailyBucketMismatches[0].CommandTotalAmount.Should().Be(250m);
        result.DailyBucketMismatches[0].QueryTotalAmount.Should().Be(200m);
    }

    [Fact]
    public void Evaluate_Should_DetectClinicBucketSeparation()
    {
        var commandBuckets = new[]
        {
            new PaymentFinanceParityEvaluator.DailyBucketSnapshot(TenantId, ClinicA, Day, "TRY", 100m, 1),
            new PaymentFinanceParityEvaluator.DailyBucketSnapshot(TenantId, ClinicB, Day, "TRY", 150m, 1)
        };
        var queryBuckets = new[]
        {
            new PaymentFinanceParityEvaluator.DailyBucketSnapshot(TenantId, ClinicA, Day, "TRY", 100m, 1)
        };

        var result = PaymentFinanceParityEvaluator.Evaluate(
            commandPaymentCount: 2,
            queryContributionCount: 1,
            commandBuckets,
            queryBuckets,
            TenantId);

        result.DailyBucketMismatchCount.Should().Be(1);
        result.DailyBucketMismatches[0].ClinicId.Should().Be(ClinicB);
    }
}
