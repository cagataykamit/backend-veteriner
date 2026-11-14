using Backend.Veteriner.Domain.Treatments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Treatments;

/// <summary>ADR-043 AK-2: tedavi gerekçeyle iptal edilir; iptal son durumdur, iptal edilmiş tedaviye kayıt bağlanmaz.</summary>
public sealed class TreatmentCancellationTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    private static Treatment New()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now.AddHours(-1), "Pansuman", "Sol arka bacak", null, null, Now);

    [Fact]
    public void Cancel_requires_reason_and_keeps_who_when_why()
    {
        var t = New();
        var user = Guid.NewGuid();

        t.Cancel(user, "kısa", Now).Error.Code.Should().Be("Treatments.Validation");
        t.IsCancelled.Should().BeFalse();
        t.EnsureAcceptsLinkedRecords().IsSuccess.Should().BeTrue();

        t.Cancel(user, " Yanlış hastaya girildi ", Now).IsSuccess.Should().BeTrue();
        t.IsCancelled.Should().BeTrue();
        t.CancelledByUserId.Should().Be(user);
        t.CancelReason.Should().Be("Yanlış hastaya girildi");
        t.CancelledAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Cancelled_treatment_is_final_and_accepts_no_links()
    {
        var t = New();
        t.Cancel(Guid.NewGuid(), "Yanlış hastaya girildi", Now).IsSuccess.Should().BeTrue();

        t.Cancel(Guid.NewGuid(), "Tekrar iptal ediliyor", Now).Error.Code.Should().Be("Treatments.AlreadyCancelled");
        t.UpdateDetails(t.ClinicId, t.PetId, null, Now, "Yeni", "Açıklama", null, null, Now).Error.Code.Should().Be("Treatments.AlreadyCancelled");
        t.EnsureAcceptsLinkedRecords().Error.Code.Should().Be("Treatments.AlreadyCancelled");
    }
}
