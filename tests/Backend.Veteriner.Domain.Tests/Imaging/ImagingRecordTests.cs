using Backend.Veteriner.Domain.Imaging;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Imaging;

/// <summary>Görüntüleme kaydı kuralları (ADR-007, IMAGING_API_CONTRACT.md §1/§4).</summary>
public sealed class ImagingRecordTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

    private static ImagingRecord Create(Guid? examinationId = null, bool finalized = false, DateTime? createdAt = null)
        => ImagingRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), examinationId, finalized,
            Now.AddHours(-1), ImagingModality.XRay, "  Toraks LL  ", "  ", Guid.NewGuid(), createdAt ?? Now).Value!;

    [Fact]
    public void Create_Should_Trim_Texts_And_Start_Active()
    {
        var record = Create();

        record.Status.Should().Be(ImagingRecordStatus.Active);
        record.Description.Should().Be("Toraks LL");
        record.ClinicalNote.Should().BeNull();
        record.AddedAfterExaminationFinalization.Should().BeFalse();
        record.CreatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Create_On_Finalized_Examination_Should_Mark_Late_Entry()
    {
        Create(Guid.NewGuid(), finalized: true).AddedAfterExaminationFinalization.Should().BeTrue();
        Create(null, finalized: true).AddedAfterExaminationFinalization.Should().BeFalse();
    }

    [Fact]
    public void Create_Should_Reject_Future_Date_Beyond_Tolerance_And_Unknown_Modality()
    {
        ImagingRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, false, Now.AddDays(2),
                ImagingModality.XRay, null, null, Guid.NewGuid(), Now)
            .Error.Code.Should().Be("ImagingRecords.Validation");
        ImagingRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, false, Now.AddHours(12),
                ImagingModality.XRay, null, null, Guid.NewGuid(), Now)
            .IsSuccess.Should().BeTrue();
        ImagingRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, false, Now,
                (ImagingModality)42, null, null, Guid.NewGuid(), Now)
            .Error.Code.Should().Be("ImagingRecords.Validation");
    }

    [Fact]
    public void Create_Should_Allow_Old_Archive_Dates()
        => ImagingRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, false, Now.AddYears(-6),
                ImagingModality.Ultrasound, null, null, Guid.NewGuid(), Now)
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Cancel_Should_Require_Reason_And_Block_Further_Changes()
    {
        var record = Create();

        record.Cancel(Guid.NewGuid(), "kısa", Now).Error.Code.Should().Be("ImagingRecords.Validation");
        record.Cancel(Guid.NewGuid(), "Yanlış hastaya yüklendi", Now).IsSuccess.Should().BeTrue();

        record.Status.Should().Be(ImagingRecordStatus.Cancelled);
        record.CancelReason.Should().Be("Yanlış hastaya yüklendi");
        record.Cancel(Guid.NewGuid(), "İkinci iptal denemesi", Now).Error.Code.Should().Be("ImagingRecords.AlreadyCancelled");
        record.UpdateDetails(null, Now, ImagingModality.CT, null, null, Now).Error.Code.Should().Be("ImagingRecords.AlreadyCancelled");
        record.EnsureAcceptsFiles().Error.Code.Should().Be("ImagingRecords.AlreadyCancelled");
    }

    [Fact]
    public void IsFrozenBy_Should_Lock_Records_Existing_At_Finalization_Only()
    {
        var before = Create(createdAt: Now.AddHours(-2));
        var after = Create(createdAt: Now.AddMinutes(5));

        before.IsFrozenBy(Now).Should().BeTrue();
        after.IsFrozenBy(Now).Should().BeFalse();
        before.IsFrozenBy(null).Should().BeFalse();
    }
}
