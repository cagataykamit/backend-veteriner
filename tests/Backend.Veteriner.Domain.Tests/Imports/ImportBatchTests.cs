using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Imports;

/// <summary>ADR-019 parti durum makinesi: izinli geçişler, geçersiz geçişte <c>Imports.InvalidState</c>, tekrar ve süre kuralları.</summary>
public sealed class ImportBatchTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ValidationId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', ImportBatch.ContentHashLength);
    private static readonly ImportTableCounts Counts = new() { Total = 3, Valid = 2, Error = 1, WillCreate = 2 };
    private const string RollbackOptions = "{\"onDuplicateClient\":\"Match\",\"rollback\":{\"requestId\":\"7d0b7c3e-0000-4000-8000-000000000001\",\"policy\":\"Fail\"}}";

    private static ImportBatch NewBatch(string? name = "Eski sistem", Guid? clinicId = null)
        => ImportBatch.Create(TenantId, clinicId, Guid.NewGuid(), name, UserId, Now).Value!;

    /// <summary>Partiyi yalnız izinli geçişlerle istenen duruma getirir.</summary>
    private static ImportBatch BatchIn(ImportBatchStatus status)
    {
        var b = NewBatch();
        void Ok(Result r) => r.IsSuccess.Should().BeTrue(r.Error.Message);

        switch (status)
        {
            case ImportBatchStatus.Uploaded:
                break;
            case ImportBatchStatus.Discarded:
                Ok(b.Discard(Now));
                break;
            case ImportBatchStatus.Expired:
                Ok(b.Expire(Now + ImportBatch.DraftLifetime));
                break;
            default:
                Ok(b.ApplyMapping("{}", null, Now));
                if (status == ImportBatchStatus.Mapped)
                    break;
                Ok(b.StartValidation(Now));
                if (status == ImportBatchStatus.Validating)
                    break;
                if (status == ImportBatchStatus.ValidationFailed)
                {
                    Ok(b.FailValidation(Now));
                    break;
                }
                Ok(b.CompleteValidation(ValidationId, Counts, Counts, Counts, Now));
                if (status == ImportBatchStatus.Validated)
                    break;
                Ok(b.StartCommit(ValidationId, Now));
                if (status == ImportBatchStatus.Committing)
                    break;
                if (status == ImportBatchStatus.CommitFailed)
                {
                    Ok(b.FailCommit(Now));
                    break;
                }
                Ok(b.CompleteCommit(Counts, Counts, Counts, Now));
                if (status == ImportBatchStatus.Committed)
                    break;
                Ok(b.StartRollback(RollbackOptions, Now));
                if (status == ImportBatchStatus.RollingBack)
                    break;
                if (status == ImportBatchStatus.RollbackFailed)
                    Ok(b.FailRollback(Now));
                else
                    Ok(b.CompleteRollback(status == ImportBatchStatus.PartiallyRolledBack, Now));
                break;
        }

        b.Status.Should().Be(status);
        return b;
    }

    public static TheoryData<string, ImportBatchStatus[], ImportBatchStatus> Transitions => new()
    {
        { "ApplyMapping", [ImportBatchStatus.Uploaded, ImportBatchStatus.Mapped, ImportBatchStatus.Validated, ImportBatchStatus.ValidationFailed], ImportBatchStatus.Mapped },
        { "StartValidation", [ImportBatchStatus.Mapped, ImportBatchStatus.Validated, ImportBatchStatus.ValidationFailed], ImportBatchStatus.Validating },
        { "CompleteValidation", [ImportBatchStatus.Validating], ImportBatchStatus.Validated },
        { "FailValidation", [ImportBatchStatus.Validating], ImportBatchStatus.ValidationFailed },
        { "StartCommit", [ImportBatchStatus.Validated], ImportBatchStatus.Committing },
        { "CompleteCommit", [ImportBatchStatus.Committing], ImportBatchStatus.Committed },
        { "FailCommit", [ImportBatchStatus.Committing], ImportBatchStatus.CommitFailed },
        { "StartRollback", [ImportBatchStatus.Committed, ImportBatchStatus.PartiallyRolledBack, ImportBatchStatus.CommitFailed, ImportBatchStatus.RollbackFailed], ImportBatchStatus.RollingBack },
        { "CompleteRollback", [ImportBatchStatus.RollingBack], ImportBatchStatus.RolledBack },
        { "CompletePartialRollback", [ImportBatchStatus.RollingBack], ImportBatchStatus.PartiallyRolledBack },
        { "FailRollback", [ImportBatchStatus.RollingBack], ImportBatchStatus.RollbackFailed },
        { "Discard", [ImportBatchStatus.Uploaded, ImportBatchStatus.Mapped, ImportBatchStatus.Validated, ImportBatchStatus.ValidationFailed], ImportBatchStatus.Discarded },
        { "Expire", [ImportBatchStatus.Uploaded, ImportBatchStatus.Mapped, ImportBatchStatus.Validated, ImportBatchStatus.ValidationFailed], ImportBatchStatus.Expired },
    };

    private static Result Apply(ImportBatch b, string operation)
    {
        var later = Now.AddDays(30);
        return operation switch
        {
            "ApplyMapping" => b.ApplyMapping("{\"columns\":{}}", null, later),
            "StartValidation" => b.StartValidation(later),
            "CompleteValidation" => b.CompleteValidation(Guid.NewGuid(), Counts, Counts, Counts, later),
            "FailValidation" => b.FailValidation(later),
            "StartCommit" => b.StartCommit(ValidationId, later),
            "CompleteCommit" => b.CompleteCommit(Counts, Counts, Counts, later),
            "FailCommit" => b.FailCommit(later),
            "StartRollback" => b.StartRollback(RollbackOptions, later),
            "CompleteRollback" => b.CompleteRollback(partial: false, later),
            "CompletePartialRollback" => b.CompleteRollback(partial: true, later),
            "FailRollback" => b.FailRollback(later),
            "Discard" => b.Discard(later),
            "Expire" => b.Expire(later),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Transition_Should_Move_Only_From_Allowed_Statuses(
        string operation, ImportBatchStatus[] allowedFrom, ImportBatchStatus target)
    {
        foreach (var from in Enum.GetValues<ImportBatchStatus>())
        {
            // Çöken doğrulamanın devralınması geçiş değil kiradır (ImportBatchValidationLeaseTests).
            var batch = BatchIn(from);

            var result = Apply(batch, operation);

            if (allowedFrom.Contains(from))
            {
                result.IsSuccess.Should().BeTrue($"{operation}: {from} → {target} izinli olmalı ({result.Error.Message})");
                batch.Status.Should().Be(target);
                batch.UpdatedAtUtc.Should().Be(Now.AddDays(30));
            }
            else
            {
                result.IsSuccess.Should().BeFalse($"{operation}: {from} durumundan geçiş reddedilmeli");
                result.Error.Code.Should().Be("Imports.InvalidState");
                batch.Status.Should().Be(from);
            }
        }
    }

    [Fact]
    public void Create_Should_Start_Uploaded_With_Trimmed_Name_And_Empty_Counts()
    {
        var clinicId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var result = ImportBatch.Create(TenantId, clinicId, requestId, "  Eski sistem 1  ", UserId, Now);

        result.IsSuccess.Should().BeTrue();
        var b = result.Value!;
        b.Status.Should().Be(ImportBatchStatus.Uploaded);
        b.Name.Should().Be("Eski sistem 1");
        b.ClinicId.Should().Be(clinicId);
        b.RequestId.Should().Be(requestId);
        b.CreatedAtUtc.Should().Be(Now);
        b.UpdatedAtUtc.Should().BeNull();
        b.ContentHash.Should().BeNull();
        b.ValidationId.Should().BeNull();
        b.IsDraft.Should().BeTrue();
        b.CustomerCounts.Should().Be(ImportTableCounts.Empty);
        b.PetCounts.Should().Be(ImportTableCounts.Empty);
        b.VaccinationCounts.Should().Be(ImportTableCounts.Empty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Create_Should_Store_Blank_Name_As_Null(string? name)
        => ImportBatch.Create(TenantId, null, Guid.NewGuid(), name, UserId, Now).Value!.Name.Should().BeNull();

    [Fact]
    public void Create_Should_Reject_Too_Long_Name_And_Empty_Ids()
    {
        var tooLong = new string('a', ImportBatch.MaxNameLength + 1);

        ImportBatch.Create(TenantId, null, Guid.NewGuid(), tooLong, UserId, Now).Error.Code.Should().Be("Imports.Validation");
        ImportBatch.Create(Guid.Empty, null, Guid.NewGuid(), null, UserId, Now).Error.Code.Should().Be("Imports.Validation");
        ImportBatch.Create(TenantId, Guid.Empty, Guid.NewGuid(), null, UserId, Now).Error.Code.Should().Be("Imports.Validation");
        ImportBatch.Create(TenantId, null, Guid.Empty, null, UserId, Now).Error.Code.Should().Be("Imports.Validation");
        ImportBatch.Create(TenantId, null, Guid.NewGuid(), null, Guid.Empty, Now).Error.Code.Should().Be("Imports.Validation");
    }

    [Fact]
    public void MatchesRequest_Should_Compare_Normalized_Name_And_Clinic()
    {
        var clinicId = Guid.NewGuid();
        var b = NewBatch(" Parti ", clinicId);

        b.MatchesRequest(clinicId, "Parti", null).Should().BeTrue();
        b.MatchesRequest(clinicId, "Başka", null).Should().BeFalse();
        b.MatchesRequest(null, "Parti", null).Should().BeFalse();
    }

    [Fact]
    public void MatchesRequest_Should_Compare_Content_Hash_Case_Insensitively()
    {
        var b = NewBatch();
        b.AttachFile(Hash, 1, 0, 0).IsSuccess.Should().BeTrue();

        b.MatchesRequest(null, "Eski sistem", Hash.ToUpperInvariant()).Should().BeTrue();
        b.MatchesRequest(null, "Eski sistem", new string('b', ImportBatch.ContentHashLength)).Should().BeFalse();
        b.MatchesRequest(null, "Eski sistem", null).Should().BeFalse();
    }

    [Fact]
    public void AttachFile_Should_Store_Normalized_Hash_And_Row_Totals_Once()
    {
        var b = NewBatch();

        b.AttachFile(Hash.ToUpperInvariant(), 5, 4, 3).IsSuccess.Should().BeTrue();

        b.ContentHash.Should().Be(Hash);
        b.CustomerCounts.Should().Be(ImportTableCounts.Empty with { Total = 5 });
        b.PetCounts.Total.Should().Be(4);
        b.VaccinationCounts.Total.Should().Be(3);
        b.AttachFile(Hash, 1, 1, 1).Error.Code.Should().Be("Imports.InvalidState");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void AttachFile_Should_Reject_Invalid_Hash(string hash)
        => NewBatch().AttachFile(hash, 1, 0, 0).Error.Code.Should().Be("Imports.Validation");

    [Fact]
    public void AttachFile_Should_Reject_Negative_Counts_And_Non_Uploaded_Batch()
    {
        NewBatch().AttachFile(Hash, -1, 0, 0).Error.Code.Should().Be("Imports.Validation");
        BatchIn(ImportBatchStatus.Mapped).AttachFile(Hash, 1, 0, 0).Error.Code.Should().Be("Imports.InvalidState");
    }

    [Theory]
    [InlineData(ImportBatchStatus.Uploaded, true)]
    [InlineData(ImportBatchStatus.Mapped, true)]
    [InlineData(ImportBatchStatus.Validated, true)]
    [InlineData(ImportBatchStatus.ValidationFailed, true)]
    [InlineData(ImportBatchStatus.Validating, false)]
    [InlineData(ImportBatchStatus.Committed, false)]
    [InlineData(ImportBatchStatus.Discarded, false)]
    public void CanMap_Should_Match_ApplyMapping_Transitions(ImportBatchStatus status, bool expected)
    {
        var b = BatchIn(status);

        b.CanMap.Should().Be(expected);
        b.ApplyMapping("{}", null, Now).IsSuccess.Should().Be(expected);
    }

    [Fact]
    public void Discard_Should_Record_Payload_Purge_Time()
    {
        var b = NewBatch();
        b.PayloadPurgedAtUtc.Should().BeNull();

        b.Discard(Now.AddHours(1)).IsSuccess.Should().BeTrue();

        b.PayloadPurgedAtUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Remapping_Validated_Batch_Should_Invalidate_ValidationId()
    {
        var b = BatchIn(ImportBatchStatus.Validated);
        b.ValidationId.Should().Be(ValidationId);
        b.ValidatedAtUtc.Should().Be(Now);

        b.ApplyMapping("{\"columns\":{\"customers\":{}}}", "{\"onDuplicateClient\":\"Match\"}", Now).IsSuccess.Should().BeTrue();

        b.Status.Should().Be(ImportBatchStatus.Mapped);
        b.ValidationId.Should().BeNull();
        b.OptionsJson.Should().Contain("Match");
    }

    [Fact]
    public void ApplyMapping_Should_Reject_Empty_Mapping()
        => NewBatch().ApplyMapping(" ", null, Now).Error.Code.Should().Be("Imports.Validation");

    [Fact]
    public void StartCommit_With_Stale_ValidationId_Should_Fail_Without_Moving()
    {
        var b = BatchIn(ImportBatchStatus.Validated);

        var result = b.StartCommit(Guid.NewGuid(), Now);

        result.Error.Code.Should().Be("Imports.ValidationStale");
        b.Status.Should().Be(ImportBatchStatus.Validated);
    }

    [Fact]
    public void Validation_And_Commit_Should_Record_Counts_And_Timestamps()
    {
        var b = BatchIn(ImportBatchStatus.Committed);

        b.CommittedAtUtc.Should().Be(Now);
        b.CustomerCounts.Should().Be(Counts);
        b.VaccinationCounts.WillCreate.Should().Be(2);
        b.IsDraft.Should().BeFalse();
    }

    [Fact]
    public void FailValidation_Should_Clear_ValidationId()
    {
        var b = BatchIn(ImportBatchStatus.Validated);
        b.StartValidation(Now).IsSuccess.Should().BeTrue();

        b.FailValidation(Now).IsSuccess.Should().BeTrue();

        b.ValidationId.Should().BeNull();
    }

    [Theory]
    [InlineData(false, ImportBatchStatus.RolledBack)]
    [InlineData(true, ImportBatchStatus.PartiallyRolledBack)]
    public void CompleteRollback_Should_Set_RolledBackAt(bool partial, ImportBatchStatus expected)
    {
        var b = BatchIn(ImportBatchStatus.RollingBack);

        b.CompleteRollback(partial, Now.AddHours(1)).IsSuccess.Should().BeTrue();

        b.Status.Should().Be(expected);
        b.RolledBackAtUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void StartRollback_Should_Store_Options_And_Reject_Empty_Options()
    {
        var b = BatchIn(ImportBatchStatus.Committed);
        b.CanStartRollback.Should().BeTrue();

        b.StartRollback(" ", Now).Error.Code.Should().Be("Imports.Validation");
        b.Status.Should().Be(ImportBatchStatus.Committed);

        b.StartRollback(RollbackOptions, Now.AddHours(1)).IsSuccess.Should().BeTrue();
        b.OptionsJson.Should().Be(RollbackOptions);
        b.CanStartRollback.Should().BeFalse();
        b.LeaseOwner.Should().BeNull();
    }

    [Theory]
    [InlineData(ImportBatchStatus.Committed, true)]
    [InlineData(ImportBatchStatus.CommitFailed, true)]
    [InlineData(ImportBatchStatus.PartiallyRolledBack, true)]
    [InlineData(ImportBatchStatus.RollbackFailed, true)]
    [InlineData(ImportBatchStatus.RollingBack, false)]
    [InlineData(ImportBatchStatus.RolledBack, false)]
    [InlineData(ImportBatchStatus.Validated, false)]
    public void CanStartRollback_Should_Match_Transition_Table(ImportBatchStatus status, bool expected)
        => BatchIn(status).CanStartRollback.Should().Be(expected);

    [Fact]
    public void Rollback_Lease_Should_Be_Claimed_Renewed_Taken_Over_And_Released()
    {
        var b = BatchIn(ImportBatchStatus.RollingBack);

        b.ClaimCommitLease("w1", Now).Error.Code.Should().Be("Imports.InvalidState", "uygulama kirası geri almada alınmaz");
        b.ClaimRollbackLease("w1", Now).IsSuccess.Should().BeTrue();
        b.IsJobLeaseActive(Now.AddSeconds(30)).Should().BeTrue();
        b.ClaimRollbackLease("w2", Now.AddSeconds(30)).Error.Code.Should().Be("Imports.AnotherJobRunning");
        b.RenewRollbackLease("w2", Now.AddSeconds(30)).Error.Code.Should().Be("Imports.LeaseLost");
        b.RenewRollbackLease("w1", Now.AddMinutes(1)).IsSuccess.Should().BeTrue();
        b.LeaseUntilUtc.Should().Be(Now.AddMinutes(1) + ImportBatch.RollbackLeaseDuration);

        // Çökme: kira dolunca başka işçi devralır, eski işçi yenileyemez.
        var expired = Now.AddMinutes(1) + ImportBatch.RollbackLeaseDuration;
        b.IsJobLeaseActive(expired).Should().BeFalse();
        b.ClaimRollbackLease("w2", expired).IsSuccess.Should().BeTrue();
        b.RenewRollbackLease("w1", expired).Error.Code.Should().Be("Imports.LeaseLost");

        b.CompleteRollback(partial: true, expired).IsSuccess.Should().BeTrue();
        b.LeaseOwner.Should().BeNull();
        b.LeaseUntilUtc.Should().BeNull();
    }

    [Fact]
    public void FailRollback_Should_Release_Lease_And_Allow_Retry()
    {
        var b = BatchIn(ImportBatchStatus.RollingBack);
        b.ClaimRollbackLease("w1", Now).IsSuccess.Should().BeTrue();

        b.FailRollback(Now).IsSuccess.Should().BeTrue();

        b.LeaseOwner.Should().BeNull();
        b.CanStartRollback.Should().BeTrue();
        b.StartRollback(RollbackOptions, Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Row_RecordRollbackResult_Should_Only_Move_Created_Or_Kept_Rows()
    {
        var created = AppliedRow(ImportRowApplyStatus.Created);
        created.RecordRollbackResult(ImportRowApplyStatus.Kept, "[]");
        created.ApplyStatus.Should().Be(ImportRowApplyStatus.Kept);
        created.RecordRollbackResult(ImportRowApplyStatus.RolledBack, null);
        created.ApplyStatus.Should().Be(ImportRowApplyStatus.RolledBack);
        created.TargetEntityId.Should().NotBeNull("hedef kimliği tarihçe için kalır");

        FluentActions.Invoking(() => created.RecordRollbackResult(ImportRowApplyStatus.Kept, null))
            .Should().Throw<InvalidOperationException>("geri alınmış satır yeniden geri alınmaz");
        FluentActions.Invoking(() => AppliedRow(ImportRowApplyStatus.Matched).RecordRollbackResult(ImportRowApplyStatus.RolledBack, null))
            .Should().Throw<InvalidOperationException>("eşleşen mevcut kayıt geri alınmaz");
        FluentActions.Invoking(() => AppliedRow(ImportRowApplyStatus.Created).RecordRollbackResult(ImportRowApplyStatus.Created, null))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ImportBatchRow AppliedRow(ImportRowApplyStatus status)
    {
        var row = ImportBatchRow.Create(Guid.NewGuid(), ImportTable.Customers, 2, "{\"AdSoyad\":\"x\"}", null);
        row.RecordApplyResult(status, "Client", Guid.NewGuid(), matched: status == ImportRowApplyStatus.Matched, Now);
        return row;
    }

    [Theory]
    [InlineData(ImportBatchStatus.Committed)]
    [InlineData(ImportBatchStatus.PartiallyRolledBack)]
    public void ReleaseReminders_Should_Be_One_Way_And_Keep_Status(ImportBatchStatus status)
    {
        var b = BatchIn(status);

        b.ReleaseReminders(Now.AddDays(14)).IsSuccess.Should().BeTrue();
        var second = b.ReleaseReminders(Now.AddDays(15));

        b.Status.Should().Be(status);
        b.RemindersReleasedAtUtc.Should().Be(Now.AddDays(14));
        second.Error.Code.Should().Be("Imports.InvalidState");
    }

    [Theory]
    [InlineData(ImportBatchStatus.Validated)]
    [InlineData(ImportBatchStatus.Committing)]
    [InlineData(ImportBatchStatus.RolledBack)]
    [InlineData(ImportBatchStatus.CommitFailed)]
    public void ReleaseReminders_Should_Require_Applied_Batch(ImportBatchStatus status)
        => BatchIn(status).ReleaseReminders(Now).Error.Code.Should().Be("Imports.InvalidState");

    [Fact]
    public void Expire_Should_Wait_For_Draft_Lifetime_From_Last_Activity()
    {
        var b = NewBatch();
        b.ApplyMapping("{}", null, Now.AddDays(3)).IsSuccess.Should().BeTrue();

        // Oluşturmadan 7 gün geçti ama son hareket 3. gün: henüz dolmadı.
        b.Expire(Now.AddDays(7)).Error.Code.Should().Be("Imports.InvalidState");
        b.Status.Should().Be(ImportBatchStatus.Mapped);

        b.Expire(Now.AddDays(10)).IsSuccess.Should().BeTrue();
        b.Status.Should().Be(ImportBatchStatus.Expired);
        b.PayloadPurgedAtUtc.Should().Be(Now.AddDays(10), "hazırlık satırları süre dolarken silinir (B5)");
    }

    [Fact]
    public void Running_Validation_Should_Not_Expire_Or_Be_Discarded()
    {
        var b = BatchIn(ImportBatchStatus.Validating);

        b.Expire(Now.AddDays(30)).Error.Code.Should().Be("Imports.InvalidState");
        b.Discard(Now.AddDays(30)).Error.Code.Should().Be("Imports.InvalidState");
        b.Status.Should().Be(ImportBatchStatus.Validating);
    }

    [Fact]
    public void Status_Sets_Should_Match_Contract()
    {
        ImportBatchStatusSets.ActiveJob.Should().BeEquivalentTo(
            [ImportBatchStatus.Validating, ImportBatchStatus.Committing, ImportBatchStatus.RollingBack]);
        ImportBatchStatusSets.ContentHashReleased.Should().BeEquivalentTo(
            [ImportBatchStatus.CommitFailed, ImportBatchStatus.RolledBack, ImportBatchStatus.Discarded, ImportBatchStatus.Expired]);
        ImportBatchStatusSets.Draft.Should().Contain(ImportBatchStatusSets.IdleDraft).And.Contain(ImportBatchStatus.Validating);
        ImportBatchStatusSets.PayloadRetained.Should().BeEquivalentTo(
        [
            ImportBatchStatus.Committed, ImportBatchStatus.CommitFailed, ImportBatchStatus.RolledBack,
            ImportBatchStatus.PartiallyRolledBack, ImportBatchStatus.RollbackFailed,
        ]);
    }

    [Fact]
    public void Row_Create_Should_Start_Pending_And_Guard_Inputs()
    {
        var batchId = Guid.NewGuid();

        var row = ImportBatchRow.Create(batchId, ImportTable.Pets, 2, "{\"HayvanAdi\":\"Pamuk\"}", null);

        row.BatchId.Should().Be(batchId);
        row.ValidationStatus.Should().Be(ImportRowValidationStatus.Pending);
        row.ApplyStatus.Should().Be(ImportRowApplyStatus.Pending);
        row.Matched.Should().BeFalse();
        FluentActions.Invoking(() => ImportBatchRow.Create(batchId, ImportTable.Pets, 0, "{}", null))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ImportBatchRow.Create(Guid.Empty, ImportTable.Pets, 1, "{}", null))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ImportBatchRow.Create(batchId, (ImportTable)9, 1, "{}", null))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
