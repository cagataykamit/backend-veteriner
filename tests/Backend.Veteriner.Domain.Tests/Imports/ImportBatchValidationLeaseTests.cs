using Backend.Veteriner.Domain.Imports;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Imports;

/// <summary>B3: doğrulama kirası, başarısız doğrulamada sayaçlar, satır sonucu ve K9 saklama zamanlaması.</summary>
public sealed class ImportBatchValidationLeaseTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly ImportTableCounts Counts = new() { Total = 5, Valid = 3, Error = 2, WillCreate = 3 };

    private static ImportBatch Mapped()
    {
        var b = ImportBatch.Create(Guid.NewGuid(), null, Guid.NewGuid(), "x", Guid.NewGuid(), Now).Value!;
        b.AttachFile(new string('a', ImportBatch.ContentHashLength), 5, 5, 5).IsSuccess.Should().BeTrue();
        b.ApplyMapping("{}", null, Now).IsSuccess.Should().BeTrue();
        return b;
    }

    private static ImportBatch Committed()
    {
        var b = Mapped();
        b.StartValidation(Now);
        var id = Guid.NewGuid();
        b.CompleteValidation(id, Counts, Counts, Counts, Now);
        b.StartCommit(id, Now);
        b.CompleteCommit(Counts, Counts, Counts, Now).IsSuccess.Should().BeTrue();
        return b;
    }

    // ---------------------------------------------------------------- Kira

    [Fact]
    public void StartValidation_Should_Take_Lease_And_Complete_Should_Release_It()
    {
        var b = Mapped();

        b.StartValidation(Now).IsSuccess.Should().BeTrue();
        b.LeaseOwner.Should().Be(ImportBatch.ValidationLeaseOwner);
        b.LeaseUntilUtc.Should().Be(Now + ImportBatch.ValidationLeaseDuration);

        b.CompleteValidation(Guid.NewGuid(), Counts, Counts, Counts, Now.AddSeconds(5)).IsSuccess.Should().BeTrue();
        b.LeaseOwner.Should().BeNull();
        b.LeaseUntilUtc.Should().BeNull();
    }

    [Fact]
    public void Active_Lease_Should_Block_Second_Validation_Until_It_Expires()
    {
        var b = Mapped();
        b.StartValidation(Now);

        b.CanStartValidation(Now.AddMinutes(4)).Should().BeFalse();
        b.StartValidation(Now.AddMinutes(4)).Error.Code.Should().Be("Imports.InvalidState");

        var afterExpiry = Now + ImportBatch.ValidationLeaseDuration;
        b.IsValidationLeaseExpired(afterExpiry).Should().BeTrue();
        b.CanStartValidation(afterExpiry).Should().BeTrue();
        b.StartValidation(afterExpiry).IsSuccess.Should().BeTrue("çöken doğrulama kira dolunca devralınır");
        b.Status.Should().Be(ImportBatchStatus.Validating);
        b.LeaseUntilUtc.Should().Be(afterExpiry + ImportBatch.ValidationLeaseDuration);
    }

    [Theory]
    [InlineData(ImportBatchStatus.Uploaded)]
    [InlineData(ImportBatchStatus.Discarded)]
    public void CanStartValidation_Should_Be_False_Outside_Mapped_Validated_Failed(ImportBatchStatus status)
    {
        var b = ImportBatch.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(), Now).Value!;
        if (status == ImportBatchStatus.Discarded)
            b.Discard(Now);

        b.Status.Should().Be(status);
        b.CanStartValidation(Now.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void FailValidation_Should_Release_Lease_And_Reset_Counts_To_Totals_Unless_Given()
    {
        var b = Mapped();
        b.StartValidation(Now);
        b.CompleteValidation(Guid.NewGuid(), Counts, Counts, Counts, Now);
        b.StartValidation(Now.AddMinutes(1));

        b.FailValidation(Now.AddMinutes(2)).IsSuccess.Should().BeTrue();

        b.LeaseUntilUtc.Should().BeNull();
        b.ValidationId.Should().BeNull();
        b.CustomerCounts.Should().Be(ImportTableCounts.Empty with { Total = 5 });
        b.PetCounts.Should().Be(ImportTableCounts.Empty with { Total = 5 });
    }

    [Fact]
    public void FailValidation_Should_Keep_Given_Counts_For_All_Rows_Invalid_Case()
    {
        var b = Mapped();
        b.StartValidation(Now);
        var allErrors = ImportTableCounts.Empty with { Total = 5, Error = 5 };

        b.FailValidation(Now, customers: allErrors).IsSuccess.Should().BeTrue();

        b.CustomerCounts.Should().Be(allErrors);
        b.PetCounts.Should().Be(ImportTableCounts.Empty with { Total = 5 });
    }

    [Fact]
    public void HasValidationResult_Should_Hold_After_First_Completed_Validation_And_Drop_On_Remap()
    {
        var b = Mapped();
        b.HasValidationResult.Should().BeFalse();
        b.StartValidation(Now);
        b.HasValidationResult.Should().BeFalse();

        b.CompleteValidation(Guid.NewGuid(), Counts, Counts, Counts, Now);
        b.HasValidationResult.Should().BeTrue();

        b.ApplyMapping("{}", null, Now.AddMinutes(1));
        b.HasValidationResult.Should().BeFalse("eşleme değişince sonuç güncel değildir");
    }

    // ---------------------------------------------------------------- Satır

    [Fact]
    public void Row_ApplyValidation_Should_Replace_Result_And_Keep_Apply_State()
    {
        var row = ImportBatchRow.Create(Guid.NewGuid(), ImportTable.Customers, 2, "{\"Ad\":\"Ayşe\"}", null);

        row.ApplyValidation(ImportRowValidationStatus.Warning, "[{\"code\":\"X\"}]", "{\"action\":\"Create\"}");

        row.ValidationStatus.Should().Be(ImportRowValidationStatus.Warning);
        row.IssuesJson.Should().Contain("X");
        row.NormalizedJson.Should().Contain("Create");
        row.ApplyStatus.Should().Be(ImportRowApplyStatus.Pending);

        row.ApplyValidation(ImportRowValidationStatus.Valid, null, "{}");
        row.IssuesJson.Should().BeNull();
        FluentActions.Invoking(() => row.ApplyValidation(ImportRowValidationStatus.Pending, null, "{}"))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Row_PurgePayload_Should_Clear_Personal_Data_Only()
    {
        var row = ImportBatchRow.Create(Guid.NewGuid(), ImportTable.Customers, 2, "{\"Ad\":\"Ayşe\"}", null);
        row.ApplyValidation(ImportRowValidationStatus.Valid, "[{\"value\":\"0532\"}]", "{\"fullName\":\"Ayşe\"}");

        row.PurgePayload();

        row.SourceJson.Should().BeNull();
        row.NormalizedJson.Should().BeNull();
        row.IssuesJson.Should().BeNull();
        row.ValidationStatus.Should().Be(ImportRowValidationStatus.Valid);
        row.RowNumber.Should().Be(2);
    }

    // ---------------------------------------------------------------- K9 saklama zamanlaması

    [Fact]
    public void PayloadPurgeDueAt_Should_Be_30_Days_After_Last_Change_For_Retained_Terminal_Statuses()
    {
        var b = Committed();

        b.PayloadPurgeDueAtUtc.Should().Be(b.UpdatedAtUtc + ImportBatch.PayloadRetention);
        ImportBatch.PayloadRetention.Should().Be(TimeSpan.FromDays(30));
    }

    [Theory]
    [InlineData(ImportBatchStatus.Uploaded)]
    [InlineData(ImportBatchStatus.Validated)]
    [InlineData(ImportBatchStatus.Discarded)]
    public void PayloadPurgeDueAt_Should_Be_Null_For_Drafts_And_Already_Purged_Discarded(ImportBatchStatus status)
    {
        var b = Mapped();
        if (status == ImportBatchStatus.Uploaded)
            b = ImportBatch.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(), Now).Value!;
        if (status == ImportBatchStatus.Validated)
        {
            b.StartValidation(Now);
            b.CompleteValidation(Guid.NewGuid(), Counts, Counts, Counts, Now);
        }

        if (status == ImportBatchStatus.Discarded)
            b.Discard(Now);

        b.Status.Should().Be(status);
        b.PayloadPurgeDueAtUtc.Should().BeNull();
    }

    [Fact]
    public void Expired_Draft_Should_Become_Purgeable_30_Days_After_Expiry()
    {
        var b = Mapped();
        var expiredAt = Now + ImportBatch.DraftLifetime;
        b.Expire(expiredAt).IsSuccess.Should().BeTrue();

        b.PayloadPurgeDueAtUtc.Should().Be(expiredAt + ImportBatch.PayloadRetention);
    }

    [Fact]
    public void PurgePayload_Should_Wait_For_Retention_And_Then_Be_Single_Use()
    {
        var b = Committed();
        var due = b.PayloadPurgeDueAtUtc!.Value;

        b.PurgePayload(due.AddSeconds(-1)).Error.Code.Should().Be("Imports.InvalidState");
        b.PayloadPurgedAtUtc.Should().BeNull();

        b.PurgePayload(due).IsSuccess.Should().BeTrue();
        b.PayloadPurgedAtUtc.Should().Be(due);
        b.PayloadPurgeDueAtUtc.Should().BeNull();
        b.PurgePayload(due.AddDays(1)).IsSuccess.Should().BeFalse();
        b.CustomerCounts.Should().Be(Counts, "sayaçlar ve özet kalır");
    }
}
