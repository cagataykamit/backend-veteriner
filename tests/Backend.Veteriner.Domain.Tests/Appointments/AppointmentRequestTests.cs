using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Appointments;

public sealed class AppointmentRequestTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = Now.AddDays(2);
    private static readonly AppointmentReason Reason = AppointmentReason.Create(TenantId, ClinicId, "Aşı", 30, true, 0, [], Now);

    private static AppointmentRequest NewLink(string phone = "05551234567", string? petName = "Pamuk", DateTime? start = null)
    {
        var result = AppointmentRequest.CreateFromLink(
            TenantId, ClinicId, new PetText(petName, "Kedi", null), Reason, start ?? Start, " Ayşe ", " Ayse@Example.COM ", phone, null, Now);
        result.IsSuccess.Should().BeTrue(result.Error.Message);
        return result.Value!;
    }

    [Fact]
    public void CreateFromLink_Should_Snapshot_Contact_Reason_And_Start_Pending_History()
    {
        var r = NewLink();

        r.Status.Should().Be(AppointmentRequestStatus.Pending);
        r.Source.Should().Be(AppointmentRequestSource.Link);
        r.ContactName.Should().Be("Ayşe");
        r.ContactEmail.Should().Be("ayse@example.com");
        r.ContactPhone.Should().Be("905551234567");
        r.ReasonName.Should().Be("Aşı");
        r.DurationMinutes.Should().Be(30);
        r.ClientId.Should().BeNull();
        r.HasFreeTextPet.Should().BeTrue();
        r.History.Should().ContainSingle(h => h.FromStatus == null && h.ToStatus == AppointmentRequestStatus.Pending && h.ActorKind == AppointmentRequestActorKind.Owner);
    }

    [Theory]
    [InlineData("123", "Pamuk")]
    [InlineData("05551234567", null)]
    public void CreateFromLink_Should_Reject_Invalid_Phone_Or_Missing_Pet(string phone, string? petName)
    {
        var result = AppointmentRequest.CreateFromLink(
            TenantId, ClinicId, new PetText(petName, null, null), Reason, Start, "Ayşe", "a@example.com", phone, null, Now);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("AppointmentRequests.Validation");
    }

    [Fact]
    public void CreateFromPortal_With_Registered_Pet_Should_Not_Require_Pet_Text()
    {
        var petId = Guid.NewGuid();
        var result = AppointmentRequest.CreateFromPortal(
            TenantId, ClinicId, Guid.NewGuid(), Guid.NewGuid(), petId, null, Reason, Start, "Ayşe", "a@example.com", null, null, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PetId.Should().Be(petId);
        result.Value.PetName.Should().BeNull();
    }

    [Fact]
    public void Create_Should_Reject_Reason_Of_Another_Clinic()
    {
        var other = AppointmentReason.Create(TenantId, Guid.NewGuid(), "Aşı", 30, true, 0, [], Now);
        AppointmentRequest.CreateFromLink(TenantId, ClinicId, new PetText("P", null, null), other, Start, "A", "a@example.com", "05551234567", null, Now)
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Approve_Should_Link_Appointment_And_Mark_Reschedule_When_Time_Differs()
    {
        var same = NewLink();
        same.Approve(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start, null, Guid.NewGuid(), Now).IsSuccess.Should().BeTrue();
        same.Status.Should().Be(AppointmentRequestStatus.Approved);
        same.WasRescheduled.Should().BeFalse();

        var moved = NewLink();
        var vet = Guid.NewGuid();
        moved.Approve(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start.AddHours(1), vet, Guid.NewGuid(), Now).IsSuccess.Should().BeTrue();
        moved.WasRescheduled.Should().BeTrue();
        moved.ApprovedVeterinarianUserId.Should().Be(vet);
        moved.History.Should().HaveCount(2);
        moved.History.Last().ActorKind.Should().Be(AppointmentRequestActorKind.Staff);

        moved.Reject("geç kaldı", Guid.NewGuid(), Now).Error!.Code.Should().Be("AppointmentRequests.NotPending");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Reject_Should_Require_Reason(string reason)
    {
        var r = NewLink();
        r.Reject(reason, Guid.NewGuid(), Now).IsSuccess.Should().BeFalse();
        r.IsPending.Should().BeTrue();
    }

    [Fact]
    public void Cancel_And_Expire_Should_Only_Close_Pending_Requests()
    {
        var r = NewLink();
        r.CancelByOwner(Now).IsSuccess.Should().BeTrue();
        r.CancelByOwner(Now).IsSuccess.Should().BeFalse();
        r.ExpireIfDue(Start.AddDays(1)).Should().BeFalse();

        var due = NewLink(start: Now.AddHours(1));
        due.ExpireIfDue(Now).Should().BeFalse("saat henüz geçmedi");
        due.ExpireIfDue(Now.AddHours(1)).Should().BeTrue();
        due.Status.Should().Be(AppointmentRequestStatus.Expired);
        due.History.Last().ActorKind.Should().Be(AppointmentRequestActorKind.System);
    }

    [Fact]
    public void Email_Verification_Should_Be_Single_Use_And_Lock_After_Failed_Attempts()
    {
        var code = AppointmentRequestEmailVerification.Issue(Guid.NewGuid(), TenantId, ClinicId, "a@example.com", "hash", Now);
        code.IsInResendCooldown(Now.AddSeconds(30)).Should().BeTrue();
        code.TryConsume(true, Now.AddMinutes(1)).Should().BeTrue();
        code.TryConsume(true, Now.AddMinutes(1)).Should().BeFalse("tek kullanımlık");

        var locked = AppointmentRequestEmailVerification.Issue(Guid.NewGuid(), TenantId, ClinicId, "a@example.com", "hash", Now);
        for (var i = 0; i < 5; i++)
            locked.TryConsume(false, Now).Should().BeFalse();
        locked.TryConsume(true, Now).Should().BeFalse("deneme hakkı bitti");

        AppointmentRequestEmailVerification.Issue(Guid.NewGuid(), TenantId, ClinicId, "a@example.com", "hash", Now)
            .TryConsume(true, Now.AddMinutes(11)).Should().BeFalse("süre doldu");
    }

    [Fact]
    public void Online_Booking_Settings_Should_Start_Disabled_And_Validate_Window()
    {
        var settings = ClinicOnlineBookingSettings.Create(TenantId, ClinicId, "abcdefghjkmn", Now);
        settings.IsEnabled.Should().BeFalse();
        settings.Update(true, 120, 30, Now).IsSuccess.Should().BeTrue();
        settings.RequestWindow(Now).Should().Be((Now.AddMinutes(120), Now.AddDays(30)));
        settings.Update(true, 60 * 24 * 3, 2, Now).IsSuccess.Should().BeFalse();

        var act = () => settings.RegenerateCode("kisa", Now);
        act.Should().Throw<ArgumentException>();
    }
}
