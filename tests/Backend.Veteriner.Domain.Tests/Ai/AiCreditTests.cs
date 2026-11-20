using Backend.Veteriner.Domain.Ai;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Ai;

/// <summary>AI-007: fon kaydı, kullanım kaydındaki kredi alanları ve ayda bir kota uyarısı.</summary>
public sealed class AiCreditTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime November = new(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime December = new(2026, 11, 30, 21, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();

    [Fact]
    public void Manual_Grant_Trims_The_Note_And_Keeps_Who_Added_It()
    {
        var userId = Guid.NewGuid();

        var grant = AiCreditGrant.CreateManual(TenantId, ClinicId, 250, "  Pilot telafisi  ", userId, Now);

        grant.Kind.Should().Be(AiCreditGrantKind.Manual);
        grant.Credits.Should().Be(250);
        grant.Note.Should().Be("Pilot telafisi");
        grant.CreatedByUserId.Should().Be(userId);
        AiCreditGrant.CreateManual(TenantId, ClinicId, 1, "   ", null, Now).Note.Should().BeNull();
        AiCreditGrant.CreateManual(TenantId, ClinicId, 1, new string('x', 300), null, Now).Note.Should().HaveLength(AiCreditGrant.NoteMaxLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(AiCreditGrant.MaxCredits + 1)]
    public void Grant_Must_Be_Positive_And_Bounded(int credits)
    {
        var act = () => AiCreditGrant.CreateManual(TenantId, ClinicId, credits, null, null, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Usage_Record_Keeps_Fund_Part_Within_Total_Credits()
    {
        AiUsageRecord Record(decimal credits, decimal fund) => AiUsageRecord.Create(
            TenantId, ClinicId, Guid.NewGuid(), AiFeature.PetHistorySummary, null, null, null, "claude-haiku-5-5", "t/1",
            100, 10, 0.0001m, credits, fund, 10, AiUsageOutcome.Succeeded, Now);

        Record(2m, 0.5m).Should().Match<AiUsageRecord>(r => r.Credits == 2m && r.FundCredits == 0.5m);
        Record(2m, 5m).FundCredits.Should().Be(2m);
        Record(-1m, -1m).Should().Match<AiUsageRecord>(r => r.Credits == 0m && r.FundCredits == 0m);
    }

    [Fact]
    public void Quota_Warning_Goes_Once_Per_Level_Per_Month()
    {
        var settings = ClinicAiSettings.Create(TenantId, ClinicId, Now);

        settings.TryMarkQuotaWarning(November, 0).Should().BeFalse();
        settings.TryMarkQuotaWarning(November, 80).Should().BeTrue();
        settings.TryMarkQuotaWarning(November, 80).Should().BeFalse();
        settings.TryMarkQuotaWarning(November, 100).Should().BeTrue();
        settings.TryMarkQuotaWarning(November, 80).Should().BeFalse();
        settings.TryMarkQuotaWarning(December, 80).Should().BeTrue("yeni ay, uyarı yeniden");
        settings.QuotaWarningMonthStartUtc.Should().Be(December);
        settings.QuotaWarningLevel.Should().Be(80);
    }
}
