using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Shared;

/// <summary>ADR-043 K4: klinik kayıtlarda gerekçeli iptal kuralı tek yerde.</summary>
public sealed class RecordCancellationRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Resolve_requires_user_and_reason_and_trims_reason()
    {
        RecordCancellationRules.Resolve(Guid.Empty, "Yanlış hasta", Now, "X.Validation").Error.Code.Should().Be("X.Validation");
        RecordCancellationRules.Resolve(Guid.NewGuid(), "kısa", Now, "X.Validation").Error.Code.Should().Be("X.Validation");
        RecordCancellationRules.Resolve(Guid.NewGuid(), new string('a', CorrectionReason.MaxLength + 1), Now, "X.Validation")
            .IsSuccess.Should().BeFalse();

        var user = Guid.NewGuid();
        var ok = RecordCancellationRules.Resolve(user, "  Yanlış hastaya açıldı ", Now, "X.Validation");
        ok.IsSuccess.Should().BeTrue();
        ok.Value!.Should().Be(new RecordCancellation(Now, user, "Yanlış hastaya açıldı"));
    }

    [Fact]
    public void Resolve_normalizes_unspecified_time_to_utc()
    {
        var unspecified = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified);

        RecordCancellationRules.Resolve(Guid.NewGuid(), "Yanlış hasta", unspecified, "X.Validation")
            .Value!.CancelledAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }
}
