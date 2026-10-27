using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Imports;

/// <summary>B4: uygulama kirası (alma, yenileme, devralma), satır uygulama sonucu tekilliği, içe aktarılan aşı fabrikası.</summary>
public sealed class ImportBatchCommitLeaseTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly ImportTableCounts Counts = new() { Total = 2, Valid = 2, WillCreate = 2 };

    private static ImportBatch Committing()
    {
        var b = ImportBatch.Create(Guid.NewGuid(), null, Guid.NewGuid(), "x", Guid.NewGuid(), Now).Value!;
        b.AttachFile(new string('a', ImportBatch.ContentHashLength), 2, 0, 0);
        b.ApplyMapping("{}", null, Now);
        b.StartValidation(Now);
        var id = Guid.NewGuid();
        b.CompleteValidation(id, Counts, Counts, Counts, Now);
        b.StartCommit(id, Now).IsSuccess.Should().BeTrue();
        return b;
    }

    [Fact]
    public void Claim_Should_Take_Lease_And_Block_Other_Worker_Until_Expiry()
    {
        var b = Committing();

        b.ClaimCommitLease("w1", Now).IsSuccess.Should().BeTrue();
        b.LeaseOwner.Should().Be("w1");
        b.LeaseUntilUtc.Should().Be(Now + ImportBatch.CommitLeaseDuration);
        b.IsCommitLeaseActive(Now.AddSeconds(30)).Should().BeTrue();

        b.ClaimCommitLease("w2", Now.AddSeconds(30)).Error.Code.Should().Be("Imports.AnotherJobRunning");
        b.ClaimCommitLease("w1", Now.AddSeconds(30)).IsSuccess.Should().BeTrue("aynı işçi yeniden alabilir");

        var expired = Now.AddSeconds(30) + ImportBatch.CommitLeaseDuration;
        b.IsCommitLeaseActive(expired).Should().BeFalse();
        b.ClaimCommitLease("w2", expired).IsSuccess.Should().BeTrue("çöken işçinin kirası dolunca devralınır");
        b.LeaseOwner.Should().Be("w2");
    }

    [Fact]
    public void Renew_Should_Fail_For_Previous_Owner_After_Takeover()
    {
        var b = Committing();
        b.ClaimCommitLease("w1", Now);
        b.RenewCommitLease("w1", Now.AddMinutes(1)).IsSuccess.Should().BeTrue();
        b.LeaseUntilUtc.Should().Be(Now.AddMinutes(1) + ImportBatch.CommitLeaseDuration);

        b.ClaimCommitLease("w2", Now.AddMinutes(10));

        b.RenewCommitLease("w1", Now.AddMinutes(10)).Error.Code.Should().Be("Imports.LeaseLost");
    }

    [Fact]
    public void Claim_Should_Require_Committing_And_Valid_Owner()
    {
        var b = ImportBatch.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(), Now).Value!;
        b.ClaimCommitLease("w1", Now).Error.Code.Should().Be("Imports.InvalidState");

        var c = Committing();
        c.ClaimCommitLease(" ", Now).Error.Code.Should().Be("Imports.Validation");
        c.ClaimCommitLease(new string('x', ImportBatch.MaxLeaseOwnerLength + 1), Now).Error.Code.Should().Be("Imports.Validation");
    }

    [Fact]
    public void Complete_And_Fail_Commit_Should_Release_Lease()
    {
        var done = Committing();
        done.ClaimCommitLease("w1", Now);
        done.CompleteCommit(Counts, Counts, Counts, Now.AddMinutes(1)).IsSuccess.Should().BeTrue();
        done.LeaseOwner.Should().BeNull();
        done.LeaseUntilUtc.Should().BeNull();
        done.CommittedAtUtc.Should().Be(Now.AddMinutes(1));

        var failed = Committing();
        failed.ClaimCommitLease("w1", Now);
        failed.FailCommit(Now.AddMinutes(1)).IsSuccess.Should().BeTrue();
        failed.Status.Should().Be(ImportBatchStatus.CommitFailed);
        failed.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public void Row_RecordApplyResult_Should_Be_Single_Use()
    {
        var row = ImportBatchRow.Create(Guid.NewGuid(), ImportTable.Customers, 2, "{\"Ad\":\"Ayşe\"}", null);
        var target = Guid.NewGuid();

        row.RecordApplyResult(ImportRowApplyStatus.Created, "Client", target, matched: false, Now);

        row.ApplyStatus.Should().Be(ImportRowApplyStatus.Created);
        row.TargetEntityType.Should().Be("Client");
        row.TargetEntityId.Should().Be(target);
        row.AppliedAtUtc.Should().Be(Now);
        FluentActions.Invoking(() => row.RecordApplyResult(ImportRowApplyStatus.Created, "Client", Guid.NewGuid(), false, Now))
            .Should().Throw<InvalidOperationException>("aynı satır iki kez yazılmaz");
    }

    [Fact]
    public void Row_RecordApplyResult_Should_Reject_Non_Apply_Statuses_And_Drop_Type_Without_Target()
    {
        var row = ImportBatchRow.Create(Guid.NewGuid(), ImportTable.Pets, 3, "{\"Ad\":\"Pamuk\"}", null);
        FluentActions.Invoking(() => row.RecordApplyResult(ImportRowApplyStatus.Pending, null, null, false, Now))
            .Should().Throw<ArgumentOutOfRangeException>();

        row.RecordApplyResult(ImportRowApplyStatus.Failed, "Pet", null, false, Now, "[{\"code\":\"Pets.X\"}]");

        row.TargetEntityType.Should().BeNull();
        row.IssuesJson.Should().Contain("Pets.X");
    }

    [Fact]
    public void Vaccination_CreateImported_Should_Be_Applied_Without_Examination_And_Carry_Batch()
    {
        var batchId = Guid.NewGuid();
        var applied = Now.AddYears(-2);

        var v = Vaccination.CreateImported(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " Kuduz ", applied, applied.AddYears(1), "Seri: A1", batchId, Now);

        v.Status.Should().Be(VaccinationStatus.Applied);
        v.ExaminationId.Should().BeNull();
        v.ImportBatchId.Should().Be(batchId);
        v.VaccineName.Should().Be("Kuduz");
        v.AppliedAtUtc.Should().Be(applied);
        FluentActions.Invoking(() => Vaccination.CreateImported(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "K", applied, null, null, Guid.Empty, Now))
            .Should().Throw<ArgumentException>();
    }
}
