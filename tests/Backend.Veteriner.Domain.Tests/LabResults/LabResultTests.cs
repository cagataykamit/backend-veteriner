using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.LabResults;

public sealed class LabResultTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid PetId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static LabResult Requested()
        => new(TenantId, ClinicId, PetId, null, null, UserId, null, " Kan sayımı ", null, null, null, Now);

    private static LabResult Resulted(string? interpretation = null)
        => new(TenantId, ClinicId, PetId, null, null, UserId, DateTime.UtcNow, "Kan sayımı", "Normal", interpretation, null, Now);

    private static Result UpdateWith(LabResult l, DateTime? date, string? text, string? interpretation = null)
        => l.UpdateDetails(ClinicId, PetId, null, null, date, "Kan sayımı", text, interpretation, null, Now);

    [Fact]
    public void Ctor_Without_Result_Should_Create_Requested_With_Server_Request_Time()
    {
        var l = Requested();

        l.Status.Should().Be(LabResultStatus.Requested);
        l.TestName.Should().Be("Kan sayımı");
        l.ResultText.Should().BeNull();
        l.ResultDateUtc.Should().BeNull();
        l.RequestedByUserId.Should().Be(UserId);
        l.RequestedAtUtc.Should().Be(Now);
        l.AwaitingInterpretation.Should().BeFalse();
    }
[Fact]    public void Ctor_With_Result_Should_Request_At_Result_Date()    {        var resultDate = DateTime.UtcNow.AddDays(-5);        var l = new LabResult(TenantId, ClinicId, PetId, null, null, UserId, resultDate, "Kan sayımı", "Normal", null, null, Now);        l.RequestedAtUtc.Should().Be(resultDate);        l.InterpretationRequired.Should().BeTrue();    }    [Fact]    public void Requested_Then_Resulted_Should_Keep_Request_Time()    {        var l = Requested();        var requestedAt = l.RequestedAtUtc;        UpdateWith(l, DateTime.UtcNow.AddDays(-2), "Normal").IsSuccess.Should().BeTrue();        l.RequestedAtUtc.Should().Be(requestedAt);    }    [Fact]    public void AwaitingInterpretation_Should_Be_False_When_Interpretation_Not_Required()    {        var l = Resulted();        typeof(LabResult).GetProperty(nameof(LabResult.InterpretationRequired))!.SetValue(l, false);        l.AwaitingInterpretation.Should().BeFalse();    }

    [Fact]
    public void Ctor_With_Result_Should_Create_Resulted_And_Await_Interpretation()
    {
        var l = Resulted();

        l.Status.Should().Be(LabResultStatus.Resulted);
        l.AwaitingInterpretation.Should().BeTrue();
        Resulted("Sağlıklı").AwaitingInterpretation.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Ctor_Should_Reject_Half_Result(bool withDate, bool withText)
    {
        var act = () => new LabResult(
            TenantId, ClinicId, PetId, null, null, UserId,
            withDate ? DateTime.UtcNow : null, "T", withText ? "metin" : null, null, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateResult_Should_Reject_Interpretation_Without_Result()
    {
        var result = LabResult.ValidateResult(null, null, "yorum");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.Validation");
    }

    [Fact]
    public void Update_Should_Resolve_Requested_When_Result_Entered()
    {
        var l = Requested();

        var result = UpdateWith(l, DateTime.UtcNow, "Sonuç");

        result.IsSuccess.Should().BeTrue();
        l.Status.Should().Be(LabResultStatus.Resulted);
        l.ResultText.Should().Be("Sonuç");
    }

    [Fact]
    public void Update_Should_Keep_Requested_When_No_Result()
    {
        var l = Requested();

        UpdateWith(l, null, null).IsSuccess.Should().BeTrue();

        l.Status.Should().Be(LabResultStatus.Requested);
    }

    [Fact]
    public void Update_Should_Not_Clear_Result_Of_Resulted()
    {
        var l = Resulted();

        var result = UpdateWith(l, null, null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.Validation");
        l.ResultText.Should().Be("Normal");
    }

    [Fact]
    public void Cancel_Should_Record_Reason_And_Block_Further_Changes()
    {
        var l = Requested();

        l.Cancel(UserId, "  Yanlış hasta için istendi  ", Now).IsSuccess.Should().BeTrue();

        l.Status.Should().Be(LabResultStatus.Cancelled);
        l.CancelReason.Should().Be("Yanlış hasta için istendi");
        l.CancelledByUserId.Should().Be(UserId);
        l.CancelledAtUtc.Should().NotBeNull();
        l.AwaitingInterpretation.Should().BeFalse();
        UpdateWith(l, DateTime.UtcNow, "x").Error.Code.Should().Be("LabResults.AlreadyCancelled");
        l.Cancel(UserId, "ikinci iptal gerekçesi", Now).Error.Code.Should().Be("LabResults.AlreadyCancelled");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kısa")]
    public void Cancel_Should_Require_Reason(string? reason)
    {
        var l = Requested();

        var result = l.Cancel(UserId, reason, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.Validation");
        l.Status.Should().Be(LabResultStatus.Requested);
    }

    [Fact]
    public void ChangesIdentity_Should_Ignore_Result_Fields()
    {
        var l = Resulted();

        l.ChangesIdentity(ClinicId, PetId, null, " Kan sayımı ").Should().BeFalse();
        l.ChangesIdentity(ClinicId, PetId, null, "Başka").Should().BeTrue();
    }
}
