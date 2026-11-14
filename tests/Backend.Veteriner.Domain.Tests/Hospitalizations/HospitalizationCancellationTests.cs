using Backend.Veteriner.Domain.Hospitalizations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Hospitalizations;

/// <summary>ADR-043 AK-5: yanlış açılan yatış gerekçeyle iptal edilir; iptal son durumdur ve yatış etkin sayılmaz.</summary>
public sealed class HospitalizationCancellationTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    private static Hospitalization Stay()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now.AddHours(-3), null, "Gözlem", null, Now);

    [Fact]
    public void Cancel_requires_reason_and_keeps_who_when_why()
    {
        var h = Stay();
        var user = Guid.NewGuid();

        h.Cancel(user, "kısa", Now).Error.Code.Should().Be("Hospitalizations.Validation");
        h.IsCancelled.Should().BeFalse();

        h.Cancel(user, " Yanlış hastaya açıldı ", Now).IsSuccess.Should().BeTrue();
        h.IsCancelled.Should().BeTrue();
        h.IsActive.Should().BeFalse();
        h.CancelledByUserId.Should().Be(user);
        h.CancelReason.Should().Be("Yanlış hastaya açıldı");
        h.CancelledAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Discharged_stay_can_be_cancelled()
    {
        var h = Stay();
        h.Discharge(Now.AddHours(-1), null, Now).IsSuccess.Should().BeTrue();

        h.Cancel(Guid.NewGuid(), "Yanlış hastaya açıldı", Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Cancelled_stay_is_final_and_cannot_change()
    {
        var h = Stay();
        h.Cancel(Guid.NewGuid(), "Yanlış hastaya açıldı", Now).IsSuccess.Should().BeTrue();

        h.Cancel(Guid.NewGuid(), "Tekrar iptal ediliyor", Now).Error.Code.Should().Be("Hospitalizations.AlreadyCancelled");
        h.Discharge(Now, null, Now).Error.Code.Should().Be("Hospitalizations.AlreadyCancelled");
        h.UpdateDetails(h.ClinicId, h.PetId, null, null, h.AdmittedAtUtc, null, "Yeni", null, null, Now)
            .Error.Code.Should().Be("Hospitalizations.AlreadyCancelled");
        h.ClearRoom().Error.Code.Should().Be("Hospitalizations.AlreadyCancelled");
    }

    [Fact]
    public void Active_expression_matches_in_memory_rule()
    {
        var active = Stay();
        var discharged = Stay();
        discharged.Discharge(Now, null, Now);
        var cancelled = Stay();
        cancelled.Cancel(Guid.NewGuid(), "Yanlış hastaya açıldı", Now);

        var isActive = Hospitalization.IsActiveExpression.Compile();
        foreach (var h in new[] { active, discharged, cancelled })
            isActive(h).Should().Be(h.IsActive);

        active.IsActive.Should().BeTrue();
        Hospitalization.IsDischargedExpression.Compile()(discharged).Should().BeTrue();
        Hospitalization.IsDischargedExpression.Compile()(cancelled).Should().BeFalse();
    }
}
