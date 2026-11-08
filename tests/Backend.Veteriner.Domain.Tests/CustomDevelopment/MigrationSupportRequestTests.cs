using Backend.Veteriner.Domain.CustomDevelopment;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.CustomDevelopment;

/// <summary>TIER-021: geçiş desteği talebi aynı kayıt ve durum akışını kullanır; gün, saat ve program zorunlu, not isteğe bağlı.</summary>
public sealed class MigrationSupportRequestTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    private static Backend.Veteriner.Domain.Shared.Result<CustomDevelopmentRequest> Create(
        MigrationMeetingDays days = MigrationMeetingDays.Monday | MigrationMeetingDays.Thursday,
        MigrationMeetingTimes times = MigrationMeetingTimes.Morning,
        string? programName = "Kolayvet",
        string? note = null,
        MigrationSupportFee fee = MigrationSupportFee.Quoted)
        => CustomDevelopmentRequest.CreateMigrationSupport(
            Guid.NewGuid(), ImportSourceProfile.Kolayvet, programName, MigrationRecordVolume.UpTo5000, days, times, note, fee, UserId, Now);

    [Fact]
    public void Create_Should_Fill_Migration_Fields_And_Start_Received()
    {
        var request = Create(note: "  Eski programda 3 şube var  ", fee: MigrationSupportFee.Free).Value!;

        request.TargetKind.Should().Be(CustomDevelopmentTargetKind.Migration);
        request.RequestTitle.Should().Be(CustomDevelopmentRequest.MigrationTitle);
        request.RequestTarget.Should().Be("Kolayvet");
        request.RequestText.Should().Be("Eski programda 3 şube var");
        request.Priority.Should().Be(CustomDevelopmentPriority.Normal);
        request.Status.Should().Be(CustomDevelopmentRequestStatus.Received);
        request.MigrationSourceProgram.Should().Be(ImportSourceProfile.Kolayvet);
        request.MigrationVolume.Should().Be(MigrationRecordVolume.UpTo5000);
        request.MigrationDays.Should().Be(MigrationMeetingDays.Monday | MigrationMeetingDays.Thursday);
        request.MigrationTimes.Should().Be(MigrationMeetingTimes.Morning);
        request.MigrationFee.Should().Be(MigrationSupportFee.Free);
    }

    [Fact]
    public void Create_Should_Allow_Empty_Note()
        => Create(note: "   ").Value!.RequestText.Should().BeEmpty();

    [Theory]
    [InlineData(MigrationMeetingDays.None, MigrationMeetingTimes.Morning)]
    [InlineData(MigrationMeetingDays.Monday, MigrationMeetingTimes.None)]
    [InlineData((MigrationMeetingDays)128, MigrationMeetingTimes.Morning)]
    [InlineData(MigrationMeetingDays.Monday, (MigrationMeetingTimes)8)]
    public void Create_Should_Require_Valid_Day_And_Time(MigrationMeetingDays days, MigrationMeetingTimes times)
        => Create(days, times).Error.Code.Should().Be(CustomDevelopmentRequest.ValidationCode);

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Create_Should_Require_Program_Name(string? programName)
        => Create(programName: programName).Error.Code.Should().Be(CustomDevelopmentRequest.ValidationCode);

    [Fact]
    public void Create_Should_Reject_Too_Long_Note()
        => Create(note: new string('a', CustomDevelopmentRequest.MaxRequestTextLength + 1))
            .Error.Code.Should().Be(CustomDevelopmentRequest.ValidationCode);

    [Fact]
    public void Custom_Development_Create_Should_Not_Accept_Migration_Kind()
        => CustomDevelopmentRequest.Create(
                Guid.NewGuid(), "Başlık", "Metin", CustomDevelopmentTargetKind.Migration, "Kolayvet", CustomDevelopmentPriority.Normal, UserId, Now)
            .Error.Code.Should().Be(CustomDevelopmentRequest.ValidationCode);

    [Fact]
    public void Migration_Request_Should_Follow_Same_Status_Flow()
    {
        var request = Create().Value!;

        request.ChangeStatus(CustomDevelopmentRequestStatus.InReview, null, null, UserId, Now).IsSuccess.Should().BeTrue();
        request.ChangeStatus(CustomDevelopmentRequestStatus.Planned, null, "Görüşme 12 Kasım 10.00", UserId, Now).IsSuccess.Should().BeTrue();

        request.ResponseNote.Should().Be("Görüşme 12 Kasım 10.00");
        request.IsOpen.Should().BeTrue();
    }
}
