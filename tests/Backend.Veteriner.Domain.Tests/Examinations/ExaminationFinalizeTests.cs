using Backend.Veteriner.Domain.Examinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Examinations;

public sealed class ExaminationFinalizeTests
{
    private static readonly DateTime ExaminedAt = new(2026, 10, 8, 10, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    private static Examination CreateDraft()
        => new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), appointmentId: null,
            ExaminedAt, "Iştahsızlık", "Bulgu", "Değerlendirme", "Not",
            weightKg: 4.25m,
            utcNow: Now);

    private static Examination CreateFinalized()
    {
        var e = CreateDraft();
        e.Finalize(UserId, Now).IsSuccess.Should().BeTrue();
        return e;
    }

    [Fact]
    public void New_examination_is_draft()
    {
        var e = CreateDraft();

        e.Status.Should().Be(ExaminationStatus.Draft);
        e.IsFinalized.Should().BeFalse();
        e.FinalizedAtUtc.Should().BeNull();
        e.FinalizedByUserId.Should().BeNull();
    }

    [Fact]
    public void Finalize_sets_status_user_and_time()
    {
        var e = CreateDraft();

        var result = e.Finalize(UserId, Now);

        result.IsSuccess.Should().BeTrue();
        e.Status.Should().Be(ExaminationStatus.Finalized);
        e.FinalizedAtUtc.Should().Be(Now);
        e.FinalizedByUserId.Should().Be(UserId);
        e.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Finalize_twice_fails_and_keeps_original_user_and_time()
    {
        var e = CreateFinalized();

        var result = e.Finalize(Guid.NewGuid(), Now.AddHours(1));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.AlreadyFinalized");
        e.FinalizedByUserId.Should().Be(UserId);
        e.FinalizedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Finalize_with_empty_user_fails()
    {
        var e = CreateDraft();

        var result = e.Finalize(Guid.Empty, Now);

        result.Error.Code.Should().Be("Examinations.Validation");
        e.Status.Should().Be(ExaminationStatus.Draft);
    }

    [Fact]
    public void Finalized_examination_rejects_content_update_and_keeps_content()
    {
        var e = CreateFinalized();

        var result = e.UpdateClinicalContent(new ExaminationClinicalUpdate(
            ExaminedAt.AddHours(1), "Yeni neden", Findings: "Yeni bulgu", WeightKg: 9m), Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Finalized");
        e.VisitReason.Should().Be("Iştahsızlık");
        e.Findings.Should().Be("Bulgu");
        e.WeightKg.Should().Be(4.25m);
        e.ExaminedAtUtc.Should().Be(ExaminedAt);
    }

    [Fact]
    public void Finalized_examination_rejects_clear_vitals()
    {
        var e = CreateFinalized();

        var result = e.UpdateClinicalContent(new ExaminationClinicalUpdate(ExaminedAt, "Iştahsızlık", ClearVitals: true), Now);

        result.Error.Code.Should().Be("Examinations.Finalized");
        e.WeightKg.Should().Be(4.25m);
    }

    [Fact]
    public void Draft_examination_still_accepts_content_update()
    {
        var e = CreateDraft();

        e.UpdateClinicalContent(new ExaminationClinicalUpdate(ExaminedAt, "Yeni neden"), Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AddAddendum_on_draft_fails()
    {
        var result = CreateDraft().AddAddendum(UserId, "Gerekçe var", "Ek not", Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFinalized");
    }

    [Fact]
    public void AddAddendum_creates_trimmed_append_only_record_without_touching_examination()
    {
        var e = CreateFinalized();
        var updatedAt = e.UpdatedAtUtc;
        var at = Now.AddMinutes(35);

        var result = e.AddAddendum(UserId, "  Kilo hatalı girildi  ", "  Doğru kilo 12,4 kg.  ", at);

        result.IsSuccess.Should().BeTrue();
        var a = result.Value!;
        a.ExaminationId.Should().Be(e.Id);
        a.TenantId.Should().Be(e.TenantId);
        a.ClinicId.Should().Be(e.ClinicId);
        a.Reason.Should().Be("Kilo hatalı girildi");
        a.Text.Should().Be("Doğru kilo 12,4 kg.");
        a.CreatedByUserId.Should().Be(UserId);
        a.CreatedAtUtc.Should().Be(at);
        e.UpdatedAtUtc.Should().Be(updatedAt);
        e.Findings.Should().Be("Bulgu");
    }

    [Theory]
    [InlineData(null, "Metin")]
    [InlineData("", "Metin")]
    [InlineData("   ", "Metin")]
    [InlineData("kısa", "Metin")]
    [InlineData("Yeterli gerekçe", null)]
    [InlineData("Yeterli gerekçe", "   ")]
    public void AddAddendum_requires_reason_and_text(string? reason, string? text)
    {
        var result = CreateFinalized().AddAddendum(UserId, reason, text, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Validation");
    }

    [Fact]
    public void AddAddendum_rejects_too_long_reason_and_text()
    {
        var e = CreateFinalized();

        e.AddAddendum(UserId, new string('a', ExaminationAddendum.MaxReasonLength + 1), "Metin", Now)
            .Error.Code.Should().Be("Examinations.Validation");
        e.AddAddendum(UserId, "Yeterli gerekçe", new string('a', ExaminationAddendum.MaxTextLength + 1), Now)
            .Error.Code.Should().Be("Examinations.Validation");
        e.AddAddendum(UserId, new string('a', ExaminationAddendum.MaxReasonLength), new string('b', ExaminationAddendum.MaxTextLength), Now)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AddAddendum_with_empty_user_fails()
    {
        CreateFinalized().AddAddendum(Guid.Empty, "Yeterli gerekçe", "Metin", Now)
            .Error.Code.Should().Be("Examinations.Validation");
    }

    [Fact]
    public void Addendum_has_no_public_mutators()
    {
        var publicMethods = typeof(ExaminationAddendum)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);
        var publicSetters = typeof(ExaminationAddendum).GetProperties()
            .Where(p => p.SetMethod?.IsPublic == true);

        publicMethods.Should().BeEmpty();
        publicSetters.Should().BeEmpty();
    }

    [Fact]
    public void EnsureAcceptsLinkedRecords_succeeds_for_draft()
    {
        CreateDraft().EnsureAcceptsLinkedRecords().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureAcceptsLinkedRecords_fails_for_finalized()
    {
        var result = CreateFinalized().EnsureAcceptsLinkedRecords();

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Finalized");
    }
}
