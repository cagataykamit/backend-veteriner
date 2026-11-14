using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Shared;

/// <summary>ADR-043 K6 / AK-10: kaydın hastası ve kliniği güncellemeyle değişmez.</summary>
public sealed class RecordSubjectRuleTests
{
    [Fact]
    public void EnsureUnchanged_rejects_pet_or_clinic_change()
    {
        var clinic = Guid.NewGuid();
        var pet = Guid.NewGuid();

        RecordSubjectRule.EnsureUnchanged(clinic, pet, clinic, pet, "X").IsSuccess.Should().BeTrue();
        RecordSubjectRule.EnsureUnchanged(clinic, pet, clinic, Guid.NewGuid(), "X").Error.Code.Should().Be("X.SubjectChangeNotAllowed");
        RecordSubjectRule.EnsureUnchanged(clinic, pet, Guid.NewGuid(), pet, "X").Error.Code.Should().Be("X.SubjectChangeNotAllowed");
    }

    [Fact]
    public void Treatment_update_keeps_its_pet()
    {
        var now = DateTime.UtcNow;
        var t = new Treatment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, now.AddHours(-1), "Pansuman", "Açıklama", null, null, now);

        t.UpdateDetails(t.ClinicId, Guid.NewGuid(), null, now, "Pansuman", "Açıklama", null, null, now)
            .Error.Code.Should().Be("Treatments.SubjectChangeNotAllowed");
        t.UpdateDetails(t.ClinicId, t.PetId, null, now, "Pansuman ve temizlik", "Açıklama", null, null, now).IsSuccess.Should().BeTrue();
    }
}
