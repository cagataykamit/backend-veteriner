using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Appointments;

/// <summary>TIER-019: hatırlatma "onayla / iptal et" bağlantısı ve sahip onayı.</summary>
public sealed class AppointmentOwnerLinkTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    private static Appointment Scheduled(DateTime? at = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at ?? Now.AddHours(24), 30, AppointmentType.Checkup);

    private static AppointmentOwnerLink LinkFor(Appointment a)
        => AppointmentOwnerLink.Create(a.TenantId, a.ClinicId, a.Id, new string('a', 64), a.ScheduledAtUtc, Now);

    [Fact]
    public void Create_Should_Expire_At_Appointment_Start_And_Reject_Past_Appointment()
    {
        var appointment = Scheduled();
        var link = LinkFor(appointment);

        link.ExpiresAtUtc.Should().Be(appointment.ScheduledAtUtc);
        link.ClosedAtUtc.Should().BeNull();
        link.UsedAction.Should().BeNull();

        var past = () => AppointmentOwnerLink.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hash", Now.AddMinutes(-1), Now);
        past.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Should_Treat_Unspecified_Kind_As_Utc_Like_Appointment()
    {
        // Veritabanından okunan randevu saati türü belirsizdir; yerel saat sayılıp kaydırılmamalı.
        var fromDb = DateTime.SpecifyKind(Now.AddHours(2), DateTimeKind.Unspecified);

        var link = AppointmentOwnerLink.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hash", fromDb, Now);

        link.ExpiresAtUtc.Should().Be(Now.AddHours(2));
        link.ExpiresAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void IsUsableFor_Should_Be_True_Only_Before_Start_For_Own_Scheduled_Appointment()
    {
        var appointment = Scheduled();
        var link = LinkFor(appointment);

        link.IsUsableFor(appointment, Now).Should().BeTrue();
        link.IsUsableFor(appointment, appointment.ScheduledAtUtc).Should().BeFalse("süre randevu saatinde dolar");
        link.IsUsableFor(Scheduled(appointment.ScheduledAtUtc), Now).Should().BeFalse("başka randevunun bağlantısı");
    }

    [Fact]
    public void Used_Link_Should_Be_Closed_For_Both_Actions()
    {
        var appointment = Scheduled();
        var link = LinkFor(appointment);

        link.MarkUsed(AppointmentOwnerLinkAction.Confirm, Now);

        link.UsedAction.Should().Be(AppointmentOwnerLinkAction.Confirm);
        link.ClosedAtUtc.Should().Be(Now);
        link.IsUsableFor(appointment, Now).Should().BeFalse("tek kullanımlık; diğer eylem de geçersiz");
    }

    [Fact]
    public void Closed_Sibling_Link_Should_Keep_No_Action()
    {
        var appointment = Scheduled();
        var sibling = LinkFor(appointment);

        sibling.Close(Now);
        sibling.Close(Now.AddMinutes(5));

        sibling.ClosedAtUtc.Should().Be(Now);
        sibling.UsedAction.Should().BeNull();
        sibling.IsUsableFor(appointment, Now).Should().BeFalse();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("complete")]
    [InlineData("noshow")]
    public void Link_Should_Be_Unusable_When_Appointment_Is_No_Longer_Scheduled(string outcome)
    {
        var appointment = Scheduled(Now.AddMinutes(30));
        var link = LinkFor(appointment);

        var result = outcome switch
        {
            "cancel" => appointment.Cancel(),
            "complete" => appointment.Complete(),
            _ => appointment.MarkNoShow(appointment.ScheduledAtUtc.AddMinutes(1)),
        };

        result.IsSuccess.Should().BeTrue();
        link.IsUsableFor(appointment, Now).Should().BeFalse();
    }

    [Fact]
    public void Link_Should_Be_Unusable_After_Reschedule()
    {
        var appointment = Scheduled();
        var link = LinkFor(appointment);

        appointment.RescheduleTo(appointment.ScheduledAtUtc.AddHours(2)).IsSuccess.Should().BeTrue();

        link.IsUsableFor(appointment, Now).Should().BeFalse("bağlantı gönderimdeki saat içindir");
    }

    [Fact]
    public void Calendar_Link_Should_Give_Calendar_But_Never_Actions()
    {
        var appointment = Scheduled();
        var calendar = AppointmentOwnerLink.Create(
            appointment.TenantId, appointment.ClinicId, appointment.Id, new string('b', 64), appointment.ScheduledAtUtc, Now,
            AppointmentOwnerLinkPurpose.Calendar);

        calendar.Purpose.Should().Be(AppointmentOwnerLinkPurpose.Calendar);
        calendar.ExpiresAtUtc.Should().Be(appointment.ScheduledAtUtc, "süre randevu saatinde dolar");
        calendar.IsCalendarUsableFor(appointment, Now).Should().BeTrue();
        calendar.IsUsableFor(appointment, Now).Should().BeFalse("takvim tokeni eylem yapamaz");
        calendar.IsCalendarUsableFor(appointment, appointment.ScheduledAtUtc).Should().BeFalse("süre doldu");
        LinkFor(appointment).Purpose.Should().Be(AppointmentOwnerLinkPurpose.Respond, "varsayılan yanıt bağlantısı");
    }

    [Fact]
    public void Confirmed_Respond_Link_Should_Still_Give_Calendar_Until_Cancel_Or_Reschedule()
    {
        var appointment = Scheduled();
        var link = LinkFor(appointment);

        link.MarkUsed(AppointmentOwnerLinkAction.Confirm, Now);
        link.IsCalendarUsableFor(appointment, Now).Should().BeTrue("takvim okumadır; onaydan sonra da alınır");

        var rescheduled = Scheduled();
        var rescheduledLink = LinkFor(rescheduled);
        rescheduled.RescheduleTo(rescheduled.ScheduledAtUtc.AddHours(1));
        rescheduledLink.IsCalendarUsableFor(rescheduled, Now).Should().BeFalse("eski bağlantı geçersiz");

        appointment.Cancel().IsSuccess.Should().BeTrue();
        link.IsCalendarUsableFor(appointment, Now).Should().BeFalse("iptal randevuda red");
    }

    [Fact]
    public void ConfirmByOwner_Should_Keep_Status_Set_Time_Once_And_Advance_Sequence()
    {
        var appointment = Scheduled();

        appointment.ConfirmByOwner(Now).IsSuccess.Should().BeTrue();
        appointment.ConfirmByOwner(Now.AddMinutes(10)).IsSuccess.Should().BeTrue();

        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.OwnerConfirmedAtUtc.Should().Be(Now);
        appointment.MutationSequence.Should().Be(1, "ikinci onay değişiklik yapmaz");
    }

    [Fact]
    public void ConfirmByOwner_Should_Reject_Past_Completed_And_Cancelled_Appointments()
    {
        Scheduled().ConfirmByOwner(Now.AddDays(2)).Error.Code.Should().Be("Appointments.InvalidStatusTransition");

        var completed = Scheduled();
        completed.Complete();
        completed.ConfirmByOwner(Now).IsSuccess.Should().BeFalse();

        var cancelled = Scheduled();
        cancelled.Cancel();
        cancelled.ConfirmByOwner(Now).IsSuccess.Should().BeFalse();
        cancelled.OwnerConfirmedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Owner_Confirmation_Should_Clear_When_Time_Changes_But_Not_When_Details_Keep_Time()
    {
        var appointment = Scheduled();
        appointment.ConfirmByOwner(Now);

        appointment.UpdateDetails(appointment.ClinicId, appointment.PetId, appointment.ScheduledAtUtc, 45, AppointmentType.Checkup, "not")
            .IsSuccess.Should().BeTrue();
        appointment.OwnerConfirmedAtUtc.Should().Be(Now);

        appointment.RescheduleTo(appointment.ScheduledAtUtc.AddHours(1));
        appointment.OwnerConfirmedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Owner_Link_Cancel_Should_Use_Standard_Cancel_Transition_With_Reason()
    {
        var appointment = Scheduled();

        appointment.Cancel(Appointment.OwnerLinkCancellationReason).IsSuccess.Should().BeTrue();

        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.Notes.Should().Be("İptal: sahip e-postadan iptal etti");
    }

    [Fact]
    public void Clinic_Cancel_Link_Should_Default_To_Enabled_And_Be_Switchable()
    {
        var settings = ClinicAppointmentSettings.Create(Guid.NewGuid(), Guid.NewGuid(), 30, 15, false, Now);
        settings.OwnerCancelLinkEnabled.Should().BeTrue();

        settings.Update(30, 15, false, ownerCancelLinkEnabled: false, Now);
        settings.OwnerCancelLinkEnabled.Should().BeFalse();
    }
}
