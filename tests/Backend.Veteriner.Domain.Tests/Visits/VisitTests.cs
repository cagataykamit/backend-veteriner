using Backend.Veteriner.Domain.Visits;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Visits;

public sealed class VisitTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    private const string Reason = "Yanlis hasta secildi";

    private static Visit CreateWaiting(Guid? appointmentId = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), appointmentId, null, Guid.NewGuid(), Now);

    private static Visit CreateInProgress()
    {
        var visit = CreateWaiting();
        visit.Start(Now.AddMinutes(5));
        return visit;
    }

    [Fact]
    public void New_Should_Be_Waiting_With_Zero_Sequence()
    {
        var visit = CreateWaiting();

        visit.CareStatus.Should().Be(VisitCareStatus.Waiting);
        visit.MutationSequence.Should().Be(0);
        visit.ArrivedAtUtc.Should().Be(Now);
        visit.IsVoided.Should().BeFalse();
        visit.AppointmentId.Should().BeNull();
    }

    [Fact]
    public void New_Should_Normalize_Unspecified_Arrival_To_Utc()
    {
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(),
            new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Unspecified));

        visit.ArrivedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void New_Should_Reject_Empty_Required_Ids()
    {
        var act = () => new Visit(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(), Now);
        act.Should().Throw<ArgumentException>();

        act = () => new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, null, null, Guid.NewGuid(), Now);
        act.Should().Throw<ArgumentException>();

        act = () => new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, null, Guid.NewGuid(), Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Start_Should_Move_Waiting_To_InProgress()
    {
        var visit = CreateWaiting();

        var result = visit.Start(Now.AddMinutes(5));

        result.IsSuccess.Should().BeTrue();
        visit.CareStatus.Should().Be(VisitCareStatus.InProgress);
        visit.StartedAtUtc.Should().Be(Now.AddMinutes(5));
        visit.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void Start_When_Already_InProgress_Should_Be_Idempotent()
    {
        var visit = CreateInProgress();
        var sequence = visit.MutationSequence;

        var result = visit.Start(Now.AddHours(1));

        result.IsSuccess.Should().BeTrue();
        visit.MutationSequence.Should().Be(sequence);
        visit.StartedAtUtc.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Start_When_Completed_Should_Fail_With_InvalidStatusTransition()
    {
        var visit = CreateInProgress();
        visit.Complete(Now.AddMinutes(30));

        var result = visit.Start(Now.AddHours(1));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Visits.InvalidStatusTransition");
    }

    [Fact]
    public void Complete_Should_Move_InProgress_To_Completed()
    {
        var visit = CreateInProgress();

        var result = visit.Complete(Now.AddMinutes(30));

        result.IsSuccess.Should().BeTrue();
        visit.CareStatus.Should().Be(VisitCareStatus.Completed);
        visit.CompletedAtUtc.Should().Be(Now.AddMinutes(30));
    }

    [Fact]
    public void Complete_When_Waiting_Should_Not_Skip_InProgress()
    {
        var visit = CreateWaiting();

        var result = visit.Complete(Now.AddMinutes(30));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Visits.InvalidStatusTransition");
        visit.CareStatus.Should().Be(VisitCareStatus.Waiting);
        visit.MutationSequence.Should().Be(0);
    }

    [Fact]
    public void Complete_When_Already_Completed_Should_Be_Idempotent()
    {
        var visit = CreateInProgress();
        visit.Complete(Now.AddMinutes(30));
        var sequence = visit.MutationSequence;

        var result = visit.Complete(Now.AddHours(2));

        result.IsSuccess.Should().BeTrue();
        visit.MutationSequence.Should().Be(sequence);
        visit.CompletedAtUtc.Should().Be(Now.AddMinutes(30));
    }

    [Fact]
    public void CorrectCareStatus_Back_To_Waiting_Should_Clear_Timestamps()
    {
        var visit = CreateInProgress();
        visit.Complete(Now.AddMinutes(30));

        var result = visit.CorrectCareStatus(VisitCareStatus.Waiting, Reason, Now.AddHours(1));

        result.IsSuccess.Should().BeTrue();
        visit.CareStatus.Should().Be(VisitCareStatus.Waiting);
        visit.StartedAtUtc.Should().BeNull();
        visit.CompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public void CorrectCareStatus_Completed_Back_To_InProgress_Should_Keep_StartedAt_And_Clear_CompletedAt()
    {
        var visit = CreateInProgress();
        visit.Complete(Now.AddMinutes(30));

        var result = visit.CorrectCareStatus(VisitCareStatus.InProgress, Reason, Now.AddHours(1));

        result.IsSuccess.Should().BeTrue();
        visit.StartedAtUtc.Should().Be(Now.AddMinutes(5));
        visit.CompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public void CorrectCareStatus_Forward_To_Completed_Should_Set_Timestamps()
    {
        var visit = CreateWaiting();

        var result = visit.CorrectCareStatus(VisitCareStatus.Completed, Reason, Now.AddHours(1));

        result.IsSuccess.Should().BeTrue();
        visit.StartedAtUtc.Should().Be(Now.AddHours(1));
        visit.CompletedAtUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void CorrectCareStatus_To_Same_Status_Should_Fail_Validation()
    {
        var visit = CreateWaiting();

        var result = visit.CorrectCareStatus(VisitCareStatus.Waiting, Reason, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Visits.Validation");
        visit.MutationSequence.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void CorrectCareStatus_Without_Valid_Reason_Should_Fail(string? reason)
    {
        var visit = CreateInProgress();

        var result = visit.CorrectCareStatus(VisitCareStatus.Waiting, reason, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Visits.Validation");
        visit.CareStatus.Should().Be(VisitCareStatus.InProgress);
    }

    [Fact]
    public void ValidateReason_Should_Reject_Over_Max_Length()
    {
        var tooLong = new string('a', Visit.MaxCorrectionReasonLength + 1);

        Visit.ValidateReason(tooLong).IsSuccess.Should().BeFalse();
        Visit.ValidateReason(new string('a', Visit.MaxCorrectionReasonLength)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void MarkAsMistaken_Should_Void_And_Keep_Record()
    {
        var visit = CreateWaiting();

        var result = visit.MarkAsMistaken($"  {Reason}  ", Now.AddMinutes(1));

        result.IsSuccess.Should().BeTrue();
        visit.IsVoided.Should().BeTrue();
        visit.VoidReason.Should().Be(Reason);
        visit.VoidedAtUtc.Should().Be(Now.AddMinutes(1));
        visit.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void MarkAsMistaken_Twice_Should_Fail_NotFound()
    {
        var visit = CreateWaiting();
        visit.MarkAsMistaken(Reason, Now);

        var result = visit.MarkAsMistaken(Reason, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Visits.NotFound");
    }

    [Fact]
    public void Voided_Visit_Should_Reject_Transitions_And_Corrections()
    {
        var visit = CreateWaiting();
        visit.MarkAsMistaken(Reason, Now);

        visit.Start(Now).Error.Code.Should().Be("Visits.NotFound");
        visit.Complete(Now).Error.Code.Should().Be("Visits.NotFound");
        visit.CorrectCareStatus(VisitCareStatus.Completed, Reason, Now).Error.Code.Should().Be("Visits.NotFound");
    }
}
