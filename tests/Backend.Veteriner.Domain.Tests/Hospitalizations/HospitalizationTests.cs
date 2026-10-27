using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Hospitalizations;

public sealed class HospitalizationTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid PetId = Guid.NewGuid();

    private static Hospitalization Stay(Guid? visitId = null, string? location = null)
        => new(TenantId, ClinicId, PetId, null, DateTime.UtcNow.AddHours(-3), null, "Gözlem", "Giriş notu", Now, visitId, location);

    private static Result UpdateWith(Hospitalization h, Guid? visitId = null, string? notes = "Giriş notu", string? location = null)
        => h.UpdateDetails(ClinicId, PetId, null, visitId, h.AdmittedAtUtc, null, "Gözlem", notes, location, Now);

    [Fact]
    public void Ctor_Should_Keep_Visit_And_Trim_Location()
    {
        var visitId = Guid.NewGuid();

        var h = Stay(visitId, "  Kafes 3 ");

        h.VisitId.Should().Be(visitId);
        h.Location.Should().Be("Kafes 3");
        h.DischargeNote.Should().BeNull();
    }

    [Fact]
    public void Ctor_Without_Optional_Parts_Should_Leave_Them_Null()
    {
        var h = new Hospitalization(TenantId, ClinicId, PetId, null, DateTime.UtcNow.AddHours(-1), null, "R", null, Now);

        h.VisitId.Should().BeNull();
        h.Location.Should().BeNull();
    }

    [Fact]
    public void UpdateDetails_Should_Leave_Location_When_Null()
    {
        var h = Stay(location: "Kafes 3");

        UpdateWith(h, location: null).IsSuccess.Should().BeTrue();

        h.Location.Should().Be("Kafes 3");
    }

    [Fact]
    public void UpdateDetails_Should_Clear_Location_When_Blank_And_Replace_When_Filled()
    {
        var h = Stay(location: "Kafes 3");

        UpdateWith(h, location: "  Oda 2 ").IsSuccess.Should().BeTrue();
        h.Location.Should().Be("Oda 2");

        UpdateWith(h, location: "   ").IsSuccess.Should().BeTrue();
        h.Location.Should().BeNull();
    }

    [Fact]
    public void UpdateDetails_Should_Set_Visit_And_Replace_Notes_Fully()
    {
        var h = Stay();
        var visitId = Guid.NewGuid();

        UpdateWith(h, visitId, notes: null).IsSuccess.Should().BeTrue();

        h.VisitId.Should().Be(visitId);
        h.Notes.Should().BeNull();
    }

    [Fact]
    public void Discharge_Should_Not_Overwrite_Admission_Notes()
    {
        var h = Stay();

        h.Discharge(DateTime.UtcNow, "Taburcu notu", Now).IsSuccess.Should().BeTrue();

        h.Notes.Should().Be("Giriş notu");
        h.DischargeNote.Should().Be("Taburcu notu");
        h.DischargedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Discharge_Should_Leave_Note_When_Null_And_Clear_When_Blank()
    {
        var h = Stay();
        h.Discharge(DateTime.UtcNow, "Not", Now).IsSuccess.Should().BeTrue();
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.DischargedAtUtc))!.SetValue(h, null);

        h.Discharge(DateTime.UtcNow, null, Now).IsSuccess.Should().BeTrue();
        h.DischargeNote.Should().Be("Not");

        typeof(Hospitalization).GetProperty(nameof(Hospitalization.DischargedAtUtc))!.SetValue(h, null);
        h.Discharge(DateTime.UtcNow, " ", Now).IsSuccess.Should().BeTrue();
        h.DischargeNote.Should().BeNull();
    }

    [Fact]
    public void Discharged_Should_Not_Be_Updatable()
    {
        var h = Stay();
        h.Discharge(DateTime.UtcNow, null, Now).IsSuccess.Should().BeTrue();

        var result = UpdateWith(h);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.AlreadyDischarged");
    }

    [Fact]
    public void SetExpectedRowVersion_Should_Reject_Empty()
    {
        var h = Stay();

        var act = () => h.SetExpectedRowVersion([]);

        act.Should().Throw<ArgumentException>();
        h.SetExpectedRowVersion(new byte[8]);
        h.RowVersion.Should().HaveCount(8);
    }
}
