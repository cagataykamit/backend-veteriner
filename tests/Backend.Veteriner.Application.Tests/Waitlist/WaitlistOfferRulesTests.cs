using Backend.Veteriner.Application.Waitlist;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Waitlist;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Waitlist;

/// <summary>TIER-018: teklif gidebilirlik kuralı (e-posta, hatırlatma tercihi), boşalan saat tanımı ve teklif e-postasının içeriği.</summary>
public sealed class WaitlistOfferRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BlockerFor_Should_Require_Email_And_Reminder_Preference()
    {
        var reachable = new Client(Guid.NewGuid(), "Ayşe Yılmaz", Now, "905551110000", "ayse@example.com");
        var noEmail = new Client(Guid.NewGuid(), "Ali Kaya", Now, "905551110001", null);
        var optedOut = new Client(Guid.NewGuid(), "Can Demir", Now, "905551110002", "can@example.com");
        optedOut.SetRemindersOptedOutByOwner(true, Now).IsSuccess.Should().BeTrue();

        WaitlistOfferService.BlockerFor(reachable).Should().Be(WaitlistOfferBlocker.None);
        WaitlistOfferService.BlockerFor(noEmail).Should().Be(WaitlistOfferBlocker.NoEmail);
        WaitlistOfferService.BlockerFor(optedOut).Should().Be(WaitlistOfferBlocker.RemindersOff, "portal ya da personel tercihi kapalı");
    }

    [Fact]
    public void IsReleased_Should_Count_Only_Future_Cancelled_Appointments()
    {
        Appointment At(DateTime when) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), when, 30, AppointmentType.Checkup);

        var cancelled = At(Now.AddHours(5));
        cancelled.Cancel().IsSuccess.Should().BeTrue();
        WaitlistOfferService.IsReleased(cancelled, Now).Should().BeTrue();

        WaitlistOfferService.IsReleased(At(Now.AddHours(5)), Now).Should().BeFalse("planlı randevu saat boşaltmaz");

        var noShow = At(Now.AddHours(-1));
        noShow.MarkNoShow(Now).IsSuccess.Should().BeTrue();
        WaitlistOfferService.IsReleased(noShow, Now).Should().BeFalse("gelmedi saat boşaltmaz");

        var pastCancelled = At(Now.AddHours(-1));
        pastCancelled.Cancel().IsSuccess.Should().BeTrue();
        WaitlistOfferService.IsReleased(pastCancelled, Now).Should().BeFalse("saati geçmiş");
    }

    [Fact]
    public void Email_Should_Carry_Minimal_Data_Link_And_Expiry_Without_Promotion()
    {
        var entry = WaitlistEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, AppointmentType.Checkup, null, null,
            new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 20), WaitlistWeekdays.None, null, null, "Gizli not", Guid.NewGuid(),
            new DateOnly(2026, 11, 9), Now).Value!;
        var offer = WaitlistOffer.Create(entry, Guid.NewGuid(), new DateTime(2026, 11, 10, 7, 0, 0, DateTimeKind.Utc), 30, null, true, null, Now).Value!;

        var body = WaitlistOfferEmail.Body("Ayşe Yılmaz", "Pamuk", "Merkez", offer, "https://portal.test/bekleme-teklifi?token=abc");

        body.Should().Contain("Sayın Ayşe Yılmaz").And.Contain("Merkez").And.Contain("Pamuk")
            .And.Contain("10 Kasım 2026 Salı 10:00", "İstanbul saati")
            .And.Contain("https://portal.test/bekleme-teklifi?token=abc")
            .And.Contain("9 Kasım 2026 Pazartesi 12:00", "teklifin bitişi")
            .And.Contain("yalnız bir kez");
        body.Should().NotContain("Gizli not");
        body.ToLowerInvariant().Should().NotContain("kampanya").And.NotContain("indirim");
        WaitlistOfferEmail.Subject.Should().NotContain("Reklam");
    }
}
