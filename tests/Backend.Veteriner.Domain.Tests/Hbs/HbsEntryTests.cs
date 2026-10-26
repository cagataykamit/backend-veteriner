using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Hbs;

/// <summary>ADR-036 Dilim 2: HBS geri kaydı kuralı (Girildi / Gerekmiyor / Girilmedi) ve reçete/aşı kaydına yazımı.</summary>
public sealed class HbsEntryTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

    private static Prescription NewPrescription()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, Now, "Reçete", "İçerik", null, null);

    private static Vaccination NewVaccination(VaccinationStatus status)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "Kuduz", status,
            status == VaccinationStatus.Applied ? Now : null, status == VaccinationStatus.Applied ? null : Now.AddDays(10), null);

    [Fact]
    public void Entered_Should_Trim_Number_And_Stamp_User_And_Time()
    {
        var entry = HbsEntryRules.Resolve(HbsEntryStatus.Entered, "  2RX4K9 ", "yok sayılır", UserId, Now, "X.HbsValidation");

        entry.IsSuccess.Should().BeTrue();
        entry.Value!.Should().Be(new HbsEntry(HbsEntryStatus.Entered, "2RX4K9", null, Now, UserId));
    }

    [Fact]
    public void Entered_Without_Number_Should_Be_Allowed()
    {
        var entry = HbsEntryRules.Resolve(HbsEntryStatus.Entered, "   ", null, UserId, Now, "X.HbsValidation");

        entry.IsSuccess.Should().BeTrue();
        entry.Value!.ReferenceNumber.Should().BeNull();
    }

    [Fact]
    public void Entered_With_Too_Long_Number_Should_Fail()
    {
        var entry = HbsEntryRules.Resolve(
            HbsEntryStatus.Entered, new string('9', HbsEntryRules.ReferenceNumberMaxLength + 1), null, UserId, Now, "X.HbsValidation");

        entry.IsSuccess.Should().BeFalse();
        entry.Error.Code.Should().Be("X.HbsValidation");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kısa")]
    public void NotRequired_Should_Require_Reason(string? reason)
    {
        var entry = HbsEntryRules.Resolve(HbsEntryStatus.NotRequired, null, reason, UserId, Now, "X.HbsValidation");

        entry.IsSuccess.Should().BeFalse();
        entry.Error.Code.Should().Be("X.HbsValidation");
    }

    [Fact]
    public void NotRequired_Should_Store_Reason_And_Drop_Number()
    {
        var entry = HbsEntryRules.Resolve(HbsEntryStatus.NotRequired, "123", " Reçetesiz ürün ", UserId, Now, "X.HbsValidation");

        entry.Value!.Should().Be(new HbsEntry(HbsEntryStatus.NotRequired, null, "Reçetesiz ürün", Now, UserId));
    }

    [Fact]
    public void NotEntered_Should_Clear_Everything()
    {
        var entry = HbsEntryRules.Resolve(HbsEntryStatus.NotEntered, "123", "gerekçe metni", UserId, Now, "X.HbsValidation");

        entry.Value!.Should().Be(new HbsEntry(HbsEntryStatus.NotEntered, null, null, null, null));
    }

    [Fact]
    public void Invalid_Status_Or_User_Should_Fail()
    {
        HbsEntryRules.Resolve((HbsEntryStatus)9, null, null, UserId, Now, "X.HbsValidation").IsSuccess.Should().BeFalse();
        HbsEntryRules.Resolve(HbsEntryStatus.Entered, null, null, Guid.Empty, Now, "X.HbsValidation").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Prescription_Should_Start_NotEntered_And_Apply_Without_Touching_Clinical_Fields()
    {
        var p = NewPrescription();
        p.HbsStatus.Should().Be(HbsEntryStatus.NotEntered);

        p.ApplyHbsEntry(HbsEntryStatus.Entered, "E-1", null, UserId, Now).IsSuccess.Should().BeTrue();

        p.HbsStatus.Should().Be(HbsEntryStatus.Entered);
        p.HbsReferenceNumber.Should().Be("E-1");
        p.HbsMarkedByUserId.Should().Be(UserId);
        p.HbsMarkedAtUtc.Should().Be(Now);
        p.UpdatedAtUtc.Should().BeNull();
        p.Content.Should().Be("İçerik");
    }

    [Fact]
    public void Prescription_Invalid_Entry_Should_Keep_Previous_Values()
    {
        var p = NewPrescription();
        p.ApplyHbsEntry(HbsEntryStatus.Entered, "E-1", null, UserId, Now);

        var result = p.ApplyHbsEntry(HbsEntryStatus.NotRequired, null, null, UserId, Now);

        result.Error.Code.Should().Be("Prescriptions.HbsValidation");
        p.HbsStatus.Should().Be(HbsEntryStatus.Entered);
        p.HbsReferenceNumber.Should().Be("E-1");
    }

    [Fact]
    public void Vaccination_Should_Accept_Entry_Only_When_Applied()
    {
        var applied = NewVaccination(VaccinationStatus.Applied);
        applied.ApplyHbsEntry(HbsEntryStatus.Entered, "ATS-77", null, UserId, Now).IsSuccess.Should().BeTrue();
        applied.HbsReferenceNumber.Should().Be("ATS-77");
        applied.UpdatedAtUtc.Should().BeNull();

        var scheduled = NewVaccination(VaccinationStatus.Scheduled);
        var result = scheduled.ApplyHbsEntry(HbsEntryStatus.Entered, "ATS-78", null, UserId, Now);
        result.Error.Code.Should().Be("Vaccinations.HbsNotApplicable");
        scheduled.HbsStatus.Should().Be(HbsEntryStatus.NotEntered);
    }
}
