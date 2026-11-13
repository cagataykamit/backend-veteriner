using Backend.Veteriner.Domain.EDocuments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.EDocuments;

/// <summary>ADR-036 AK-4 (2026-11-11): otomatik e-belge ayarı, açılış anı ve gün sonu çalışma kuralı.</summary>
public sealed class ClinicEDocumentSettingsTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    private static ClinicEDocumentSettings New() => ClinicEDocumentSettings.Create(Guid.NewGuid(), Guid.NewGuid(), UserId, Now);

    [Fact]
    public void Defaults_Should_Be_Off_At_23_And_Unchecked()
    {
        var s = New();

        s.AutoIssueMode.Should().Be(EDocumentAutoIssueMode.Off);
        s.EndOfDayTime.Should().Be(new TimeOnly(23, 0));
        s.IssueOnPaymentByDefault.Should().BeFalse();
        s.AutoIssueEnabledAtUtc.Should().BeNull();
    }

    [Fact]
    public void Enabling_Should_Stamp_Start_Once_And_Disabling_Should_Clear_It()
    {
        var s = New();

        s.Update(EDocumentAutoIssueMode.OnPayment, new TimeOnly(22, 0), true, UserId, Now).IsSuccess.Should().BeTrue();
        s.AutoIssueEnabledAtUtc.Should().Be(Now);

        s.Update(EDocumentAutoIssueMode.EndOfDay, new TimeOnly(22, 0), true, UserId, Now.AddHours(1));
        s.AutoIssueEnabledAtUtc.Should().Be(Now, "mod değişse de açık kaldıkça başlangıç değişmez (geçmiş ücretler toplu kesilmez)");

        s.Update(EDocumentAutoIssueMode.Off, new TimeOnly(22, 0), true, UserId, Now.AddHours(2));
        s.AutoIssueEnabledAtUtc.Should().BeNull();
    }

    [Fact]
    public void EndOfDay_Should_Be_Due_Once_Per_Day_After_Time()
    {
        var s = New();
        s.Update(EDocumentAutoIssueMode.EndOfDay, new TimeOnly(21, 30), false, UserId, Now);
        var today = new DateOnly(2026, 11, 11);

        s.IsEndOfDayDue(today, new TimeOnly(21, 0)).Should().BeFalse();
        s.IsEndOfDayDue(today, new TimeOnly(21, 30)).Should().BeTrue();
        s.MarkEndOfDayRun(today);
        s.IsEndOfDayDue(today, new TimeOnly(23, 0)).Should().BeFalse();
        s.IsEndOfDayDue(today.AddDays(1), new TimeOnly(21, 45)).Should().BeTrue();
    }

    [Fact]
    public void Invalid_Mode_Should_Be_Rejected()
        => New().Update((EDocumentAutoIssueMode)9, new TimeOnly(23, 0), false, UserId, Now).Error.Code.Should().Be("EDocuments.Validation");

    [Fact]
    public void Pause_Should_Apply_Once_Only_When_Automatic_And_Clear_On_Save()
    {
        var s = New();
        s.PauseAutoIssue("Kontör bitti.", Now).Should().BeFalse("otomatik kapalıyken durdurulacak bir şey yok");

        s.Update(EDocumentAutoIssueMode.OnPayment, new TimeOnly(23, 0), false, UserId, Now);
        s.PauseAutoIssue("Kontör bitti.", Now.AddMinutes(5)).Should().BeTrue();
        s.IsAutoIssuePaused.Should().BeTrue();
        s.AutoIssuePauseReason.Should().Be("Kontör bitti.");
        s.PauseAutoIssue("Başka hata", Now.AddMinutes(6)).Should().BeFalse("bildirim bir kez gider");
        s.AutoIssuePauseReason.Should().Be("Kontör bitti.");

        s.Update(EDocumentAutoIssueMode.OnPayment, new TimeOnly(23, 0), false, UserId, Now.AddHours(1));
        s.IsAutoIssuePaused.Should().BeFalse();
        s.AutoIssuePauseReason.Should().BeNull();
        s.AutoIssueEnabledAtUtc.Should().Be(Now, "sürdürmek başlangıcı değiştirmez; durduğu sürede ödenen ücretler de kesilir");
    }
}
