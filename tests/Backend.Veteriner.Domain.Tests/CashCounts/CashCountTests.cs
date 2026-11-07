using Backend.Veteriner.Domain.CashCounts;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.CashCounts;

/// <summary>TIER-008 ilk dilimi: fark hesabı, tutar ve açıklama kuralları, düzeltmenin kaydı ezmemesi, iptal.</summary>
public sealed class CashCountTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 8);
    private static readonly Guid UserId = Guid.NewGuid();

    private static CashCount New(decimal opening = 500m, decimal expected = 1_250.50m, decimal counted = 1_750.50m, string? note = null)
        => CashCount.Create(Guid.NewGuid(), Guid.NewGuid(), Today, Today, opening, expected, counted, note, UserId, Now).Value!;

    [Fact]
    public void Create_Should_Compute_Expected_Closing_And_Difference()
    {
        var count = CashCount.Create(Guid.NewGuid(), Guid.NewGuid(), Today, Today, 500m, 1_250.50m, 1_740.25m, "  Para üstü eksik  ", UserId, Now).Value!;

        count.ExpectedClosingAmount.Should().Be(1_750.50m);
        count.DifferenceAmount.Should().Be(-10.25m);
        count.Note.Should().Be("Para üstü eksik");
        count.Currency.Should().Be("TRY");
        count.CreatedByUserId.Should().Be(UserId);
        count.CreatedAtUtc.Should().Be(Now);
        count.IsVoided.Should().BeFalse();
    }

    [Fact]
    public void Create_Should_Allow_No_Note_When_There_Is_No_Difference_And_Negative_Expected()
    {
        // Dünkü tahsilat bugün iptal edildiyse günün nakit neti eksi olabilir.
        var count = New(opening: 300m, expected: -100m, counted: 200m);

        count.DifferenceAmount.Should().Be(0m);
        count.Note.Should().BeNull();
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, -0.01)]
    [InlineData(0.001, 100)]
    [InlineData(100, 10_000_000.01)]
    public void Create_Should_Reject_Invalid_Amounts(double opening, double counted)
    {
        var result = CashCount.Create(Guid.NewGuid(), Guid.NewGuid(), Today, Today, (decimal)opening, 0m, (decimal)counted, "not", UserId, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(CashCount.ValidationCode);
    }

    [Fact]
    public void Create_Should_Require_Note_When_There_Is_A_Difference()
    {
        var result = CashCount.Create(Guid.NewGuid(), Guid.NewGuid(), Today, Today, 0m, 100m, 90m, "   ", UserId, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(CashCount.ValidationCode);
    }

    [Fact]
    public void Create_Should_Reject_Future_Day_And_Long_Note()
    {
        CashCount.Create(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(1), Today, 0m, 0m, 0m, null, UserId, Now)
            .IsSuccess.Should().BeFalse();
        CashCount.Create(Guid.NewGuid(), Guid.NewGuid(), Today, Today, 0m, 0m, 0m, new string('a', CashCount.MaxNoteLength + 1), UserId, Now)
            .IsSuccess.Should().BeFalse();
        CashCount.Create(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(-30), Today, 0m, 0m, 0m, null, UserId, Now)
            .IsSuccess.Should().BeTrue("geçmiş gün sayımı sonradan girilebilir");
    }

    [Fact]
    public void Correct_Should_Void_The_Old_Record_And_Return_A_Linked_Replacement()
    {
        var original = New();
        var corrector = Guid.NewGuid();
        var later = Now.AddMinutes(30);

        var result = original.Correct(500m, 1_300.50m, 1_800.50m, null, "Geç gelen nakit tahsilat", corrector, Today, later);

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        var replacement = result.Value!;
        replacement.Id.Should().NotBe(original.Id);
        replacement.CorrectsCashCountId.Should().Be(original.Id);
        replacement.TenantId.Should().Be(original.TenantId);
        replacement.ClinicId.Should().Be(original.ClinicId);
        replacement.BusinessDate.Should().Be(original.BusinessDate);
        replacement.CreatedByUserId.Should().Be(corrector);
        replacement.DifferenceAmount.Should().Be(0m);

        original.IsVoided.Should().BeTrue();
        original.VoidedByUserId.Should().Be(corrector);
        original.VoidReason.Should().Be("Geç gelen nakit tahsilat");
        original.CountedAmount.Should().Be(1_750.50m, "eski kaydın tutarları ezilmez");
        original.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void Correct_Should_Change_Nothing_When_Invalid_Or_Unchanged()
    {
        var original = New();

        original.Correct(500m, 1_250.50m, 1_750.50m, null, "Aynı değerler", UserId, Today, Now).Error.Code.Should().Be(CashCount.ValidationCode);
        original.Correct(500m, 1_250.50m, 1_700m, null, "Fark notsuz", UserId, Today, Now).Error.Code.Should().Be(CashCount.ValidationCode);
        original.Correct(500m, 1_250.50m, 1_700m, "Eksik", "kısa", UserId, Today, Now).Error.Code.Should().Be(CashCount.ValidationCode);

        original.IsVoided.Should().BeFalse();
        original.MutationSequence.Should().Be(0);
    }

    [Fact]
    public void Void_Should_Need_Reason_And_Happen_Once()
    {
        var count = New();

        count.Void(UserId, "yok", Now).IsSuccess.Should().BeFalse();
        count.Void(UserId, "Yanlış kliniğe girildi", Now).IsSuccess.Should().BeTrue();
        count.Void(UserId, "Yanlış kliniğe girildi", Now).Error.Code.Should().Be("CashCounts.AlreadyVoided");
        count.Correct(1m, 0m, 1m, null, "Düzeltme denemesi", UserId, Today, Now).Error.Code.Should().Be("CashCounts.AlreadyVoided");
        count.MutationSequence.Should().Be(1);
    }
}
