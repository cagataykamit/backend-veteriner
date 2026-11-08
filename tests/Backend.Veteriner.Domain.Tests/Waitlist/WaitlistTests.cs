using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.Waitlist;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Waitlist;

/// <summary>TIER-018: bekleme kaydının uygunluk kuralı, sıra, teklif süresi ve durum akışı, teklif bağlantısı ve ileti türü.</summary>
public sealed class WaitlistTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static WaitlistEntry Entry(
        Guid? vet = null,
        int? duration = null,
        DateOnly? from = null,
        DateOnly? to = null,
        WaitlistWeekdays days = WaitlistWeekdays.None,
        TimeOnly? fromTime = null,
        TimeOnly? toTime = null,
        Guid? clinicId = null,
        Guid? tenantId = null)
    {
        var result = WaitlistEntry.Create(
            tenantId ?? Guid.NewGuid(), clinicId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), vet, AppointmentType.Checkup, null, duration,
            from ?? Today, to ?? Today.AddDays(14), days, fromTime, toTime, "  not  ", Guid.NewGuid(), Today, Now);
        result.IsSuccess.Should().BeTrue(result.IsSuccess ? null : result.Error.Message);
        return result.Value!;
    }

    // 2026-11-11 Çarşamba 10:00 yerel.
    private static readonly DateTime WednesdayTen = new(2026, 11, 11, 10, 0, 0);

    [Fact]
    public void Create_Should_Start_Waiting_Order_By_Creation_And_Trim_Note()
    {
        var entry = Entry();

        entry.Status.Should().Be(WaitlistEntryStatus.Waiting);
        entry.SortKey.Should().Be(Now.Ticks, "sıra önce eklenme zamanıdır");
        entry.Note.Should().Be("not");
    }

    [Theory]
    [InlineData(-1, 0, "Başlangıç tarihi bugünden önce olamaz.")]
    [InlineData(3, 2, "Bitiş tarihi başlangıçtan önce olamaz.")]
    [InlineData(0, 181, "Tarih aralığı en çok 180 gün olabilir.")]
    public void Create_Should_Reject_Invalid_Date_Range(int fromOffset, int toOffset, string message)
    {
        var result = WaitlistEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, AppointmentType.Checkup, null, null,
            Today.AddDays(fromOffset), Today.AddDays(toOffset), WaitlistWeekdays.None, null, null, null, Guid.NewGuid(), Today, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(WaitlistEntry.ValidationCode);
        result.Error.Message.Should().Be(message);
    }

    [Fact]
    public void Create_Should_Reject_Reversed_Time_Range()
    {
        var result = WaitlistEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, AppointmentType.Checkup, null, null, Today, Today,
            WaitlistWeekdays.None, new TimeOnly(14, 0), new TimeOnly(9, 0), null, Guid.NewGuid(), Today, Now);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Matches_Should_Apply_Vet_Date_Day_Time_And_Duration_Preferences()
    {
        var vet = Guid.NewGuid();

        Entry().Matches(WednesdayTen, 30, null).Should().BeTrue("tercih yoksa her saat uyar");
        Entry(vet: vet).Matches(WednesdayTen, 30, vet).Should().BeTrue();
        Entry(vet: vet).Matches(WednesdayTen, 30, Guid.NewGuid()).Should().BeFalse("başka hekim");
        Entry(vet: vet).Matches(WednesdayTen, 30, null).Should().BeFalse("hekimsiz saat hekim isteyen kayda uymaz");
        Entry(from: Today.AddDays(3)).Matches(WednesdayTen, 30, null).Should().BeFalse("tarih aralığından önce");
        Entry(to: Today.AddDays(1)).Matches(WednesdayTen, 30, null).Should().BeFalse("tarih aralığından sonra");
        Entry(days: WaitlistWeekdays.Wednesday | WaitlistWeekdays.Friday).Matches(WednesdayTen, 30, null).Should().BeTrue();
        Entry(days: WaitlistWeekdays.Monday).Matches(WednesdayTen, 30, null).Should().BeFalse("seçili gün değil");
        Entry(fromTime: new TimeOnly(9, 0), toTime: new TimeOnly(10, 30)).Matches(WednesdayTen, 30, null).Should().BeTrue();
        Entry(fromTime: new TimeOnly(9, 0), toTime: new TimeOnly(10, 15)).Matches(WednesdayTen, 30, null).Should().BeFalse("saat aralığa sığmıyor");
        Entry(fromTime: new TimeOnly(11, 0)).Matches(WednesdayTen, 30, null).Should().BeFalse("saat tercihinden önce");
        Entry(duration: 45).Matches(WednesdayTen, 30, null).Should().BeFalse("kaydın süresi saate sığmıyor");
        Entry(duration: 20).Matches(WednesdayTen, 30, null).Should().BeTrue();
    }

    [Fact]
    public void Closed_Or_Booked_Entry_Should_Not_Match_And_Cannot_Close_Twice()
    {
        var closed = Entry();
        closed.Close(Guid.NewGuid(), "telefonla randevu aldı", Now).IsSuccess.Should().BeTrue();
        closed.Matches(WednesdayTen, 30, null).Should().BeFalse();
        closed.Close(Guid.NewGuid(), null, Now).Error.Code.Should().Be("Waitlist.EntryNotOpen");

        var booked = Entry();
        var appointmentId = Guid.NewGuid();
        booked.MarkBooked(appointmentId, Now).IsSuccess.Should().BeTrue();
        booked.Status.Should().Be(WaitlistEntryStatus.Booked);
        booked.BookedAppointmentId.Should().Be(appointmentId);
        booked.MarkBooked(Guid.NewGuid(), Now).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void SwapOrderWith_Should_Exchange_Sort_Keys_Only_Within_Same_Clinic()
    {
        var tenant = Guid.NewGuid();
        var clinic = Guid.NewGuid();
        var first = Entry(tenantId: tenant, clinicId: clinic);
        var second = WaitlistEntry.Create(
            tenant, clinic, Guid.NewGuid(), Guid.NewGuid(), null, AppointmentType.Other, null, null, Today, Today, WaitlistWeekdays.None,
            null, null, null, Guid.NewGuid(), Today, Now.AddMinutes(5)).Value!;
        var (firstKey, secondKey) = (first.SortKey, second.SortKey);

        second.SwapOrderWith(first).IsSuccess.Should().BeTrue();
        second.SortKey.Should().Be(firstKey);
        first.SortKey.Should().Be(secondKey);

        Entry(tenantId: tenant).SwapOrderWith(first).IsSuccess.Should().BeFalse("başka klinik");
    }

    [Fact]
    public void ComputeExpiry_Should_Be_Two_Hours_Or_One_Hour_Before_Start_And_Null_When_Too_Late()
    {
        WaitlistOffer.ComputeExpiry(Now.AddHours(5), Now).Should().Be(Now.AddHours(2), "randevuya 2 saatten çok var");
        WaitlistOffer.ComputeExpiry(Now.AddMinutes(150), Now).Should().Be(Now.AddMinutes(90), "randevudan 1 saat önce");
        WaitlistOffer.ComputeExpiry(Now.AddMinutes(75), Now).Should().Be(Now.AddMinutes(15), "en kısa pencere");
        WaitlistOffer.ComputeExpiry(Now.AddMinutes(70), Now).Should().BeNull("15 dakikadan kısa teklif gönderilmez");
        WaitlistOffer.ComputeExpiry(Now.AddMinutes(30), Now).Should().BeNull();
    }

    [Fact]
    public void Offer_Should_Allow_Single_Transition_From_Open()
    {
        var entry = Entry();
        var offer = WaitlistOffer.Create(entry, Guid.NewGuid(), Now.AddDays(1), 30, null, true, null, Now).Value!;
        offer.Status.Should().Be(WaitlistOfferStatus.Open);
        offer.ExpiresAtUtc.Should().Be(Now.AddHours(2));

        offer.Accept(Guid.NewGuid(), Now.AddMinutes(10)).IsSuccess.Should().BeTrue();
        offer.Status.Should().Be(WaitlistOfferStatus.Accepted);
        offer.Decline(Now.AddMinutes(11)).Error.Code.Should().Be("WaitlistOffers.NotOpen");
        offer.MarkSlotTaken(Now.AddMinutes(11)).IsSuccess.Should().BeFalse();
        offer.Withdraw(Now.AddMinutes(11)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Offer_Should_Not_Be_Accepted_After_Expiry_And_Expire_Only_When_Due()
    {
        var offer = WaitlistOffer.Create(Entry(), Guid.NewGuid(), Now.AddDays(1), 30, null, true, null, Now).Value!;

        offer.Expire(Now.AddMinutes(119)).IsSuccess.Should().BeFalse("süre dolmadı");
        offer.Accept(Guid.NewGuid(), Now.AddHours(2)).IsSuccess.Should().BeFalse("süre doldu");
        offer.Expire(Now.AddHours(2)).IsSuccess.Should().BeTrue();
        offer.Status.Should().Be(WaitlistOfferStatus.Expired);
    }

    [Fact]
    public void Offer_Should_Not_Be_Created_For_Closed_Entry_Or_Too_Late_Slot()
    {
        var closed = Entry();
        closed.Close(Guid.NewGuid(), null, Now);
        WaitlistOffer.Create(closed, Guid.NewGuid(), Now.AddDays(1), 30, null, true, null, Now).Error.Code.Should().Be("Waitlist.EntryNotOpen");
        WaitlistOffer.Create(Entry(), Guid.NewGuid(), Now.AddMinutes(40), 30, null, true, null, Now).Error.Code.Should().Be("WaitlistOffers.TooLate");
    }

    [Fact]
    public void Offer_Link_Should_Expire_With_Offer_And_Never_Serve_Appointment_Actions_Or_Calendar()
    {
        var source = new Appointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1), 30, AppointmentType.Checkup);
        var link = AppointmentOwnerLink.CreateForWaitlistOffer(
            source.TenantId, source.ClinicId, source.Id, Guid.NewGuid(), new string('b', 64), Now.AddHours(2), Now);

        link.Purpose.Should().Be(AppointmentOwnerLinkPurpose.WaitlistOffer);
        link.IsUsableForWaitlistOffer(Now.AddMinutes(119)).Should().BeTrue();
        link.IsUsableForWaitlistOffer(Now.AddHours(2)).Should().BeFalse();
        link.IsUsableFor(source, Now).Should().BeFalse("teklif bağlantısı randevuyu onaylayamaz ya da iptal edemez");
        link.IsCalendarUsableFor(source, Now).Should().BeFalse("takvim dosyası vermez");

        link.Close(Now);
        link.IsUsableForWaitlistOffer(Now).Should().BeFalse("tek kullanımlık");
    }

    [Fact]
    public void Waitlist_Offer_Should_Be_Reminder_Category_Email_Only_And_Follow_Preference()
    {
        OwnerNotificationChannelPolicy.CategoryOf(OwnerNotificationType.WaitlistOffer).Should().Be(OwnerNotificationCategory.Reminder, "ticari değil (ADR-046)");
        OwnerNotificationChannelPolicy.ShouldSend(OwnerNotificationType.WaitlistOffer, OwnerNotificationChannel.Email, false, recipientOptedIn: true).Should().BeTrue();
        OwnerNotificationChannelPolicy.ShouldSend(OwnerNotificationType.WaitlistOffer, OwnerNotificationChannel.Email, false, recipientOptedIn: false).Should().BeFalse();
        OwnerNotificationChannelPolicy.ModeOf(OwnerNotificationType.WaitlistOffer, OwnerNotificationChannel.Push, true).Should().Be(OwnerNotificationChannelMode.Disabled);
    }
}
