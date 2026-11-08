using Backend.Veteriner.Domain.DataExports;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.DataExports;

public sealed class DataExportJobTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string Worker = "export:test:1";

    private static DataExportJob Running()
    {
        var job = DataExportJob.Create(TenantId, UserId, Now);
        job.ClaimLease(Worker, Now).IsSuccess.Should().BeTrue();
        return job;
    }

    [Fact]
    public void Create_Should_Queue_Open_Job_Without_File()
    {
        var job = DataExportJob.Create(TenantId, UserId, Now);

        job.Status.Should().Be(DataExportJobStatus.Queued);
        job.IsOpen.Should().BeTrue();
        job.RequestedAtUtc.Should().Be(Now);
        job.StorageKey.Should().BeNull();
        job.IsDownloadable(Now).Should().BeFalse();
    }

    [Fact]
    public void Claim_Should_Count_Attempt_And_Reject_Other_Worker_While_Lease_Active()
    {
        var job = Running();

        job.Status.Should().Be(DataExportJobStatus.Running);
        job.Attempts.Should().Be(1);
        job.StartedAtUtc.Should().Be(Now);
        job.ClaimLease("export:other", Now.AddMinutes(1)).Error.Code.Should().Be("DataExports.AnotherJobRunning");

        // Kira dolunca (çöken işçi) başka işçi devralır; yeni deneme sayılır, başlangıç anı korunur.
        var takeover = Now + DataExportJob.LeaseDuration;
        job.ClaimLease("export:other", takeover).IsSuccess.Should().BeTrue();
        job.Attempts.Should().Be(2);
        job.StartedAtUtc.Should().Be(Now);
        job.RenewLease(Worker, takeover).Error.Code.Should().Be("DataExports.LeaseLost");
    }

    [Fact]
    public void Claim_Should_Stop_After_Max_Attempts()
    {
        var job = DataExportJob.Create(TenantId, UserId, Now);
        var at = Now;
        for (var i = 0; i < DataExportJob.MaxAttempts; i++)
        {
            job.ClaimLease($"export:w{i}", at).IsSuccess.Should().BeTrue();
            at += DataExportJob.LeaseDuration;
        }

        job.ClaimLease("export:last", at).Error.Code.Should().Be("DataExports.InvalidState");
        job.MarkFailed(at).IsSuccess.Should().BeTrue();
        job.Status.Should().Be(DataExportJobStatus.Failed);
        job.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void MarkReady_Should_Set_Retention_From_Single_Constant_And_Release_Lease()
    {
        var job = Running();
        var readyAt = Now.AddMinutes(3);

        job.MarkReady(Worker, "key", 1024, readyAt).IsSuccess.Should().BeTrue();

        job.Status.Should().Be(DataExportJobStatus.Ready);
        job.CompletedAtUtc.Should().Be(readyAt);
        job.ExpiresAtUtc.Should().Be(readyAt + DataExportJob.Retention);
        DataExportJob.Retention.Should().Be(TimeSpan.FromDays(7));
        job.LeaseOwner.Should().BeNull();
        job.IsDownloadable(readyAt.AddDays(6)).Should().BeTrue();
        job.IsDownloadable(readyAt + DataExportJob.Retention).Should().BeFalse();
    }

    [Fact]
    public void MarkReady_Should_Reject_Worker_Without_Lease()
    {
        var job = Running();

        job.MarkReady("export:other", "key", 10, Now).Error.Code.Should().Be("DataExports.LeaseLost");
        job.Status.Should().Be(DataExportJobStatus.Running);
    }

    [Fact]
    public void Expire_Should_Only_Close_Ready_Job_After_Retention_And_Clear_Key()
    {
        var job = Running();
        job.MarkReady(Worker, "key", 10, Now).IsSuccess.Should().BeTrue();

        job.Expire(Now.AddDays(1)).IsSuccess.Should().BeFalse();
        job.IsExpiryDue(Now + DataExportJob.Retention).Should().BeTrue();
        job.Expire(Now + DataExportJob.Retention).IsSuccess.Should().BeTrue();

        job.Status.Should().Be(DataExportJobStatus.Expired);
        job.StorageKey.Should().BeNull();
        job.CompletedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void StorageKey_Should_Be_Tenant_Scoped_Export_Path()
    {
        var jobId = Guid.NewGuid();

        DataExportJob.BuildStorageKey(TenantId, Now, jobId).Should().Be($"{TenantId:N}/exports/2026/11/{jobId:N}");
    }

    [Fact]
    public void Open_Statuses_Should_Be_Queued_And_Running()
        => DataExportJob.OpenStatuses.Should().BeEquivalentTo([DataExportJobStatus.Queued, DataExportJobStatus.Running]);
}
