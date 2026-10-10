using Backend.Veteriner.Domain.Pets;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Pets;

public sealed class PetAlertsTests
{
    private static Pet CreatePet() => new(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", Guid.NewGuid());

    [Fact]
    public void New_Pet_Should_Have_No_Alerts()
    {
        var pet = CreatePet();

        pet.AlertFlags.Should().Be(PetAlertFlags.None);
        pet.AlertNote.Should().BeNull();
    }

    [Fact]
    public void ApplyAlerts_Should_Set_Flags_And_Trimmed_Note()
    {
        var pet = CreatePet();

        var result = pet.ApplyAlerts(["Allergy", "aggressive", "Allergy"], "  Penisiline alerji  ");

        result.IsSuccess.Should().BeTrue();
        pet.AlertFlags.Should().Be(PetAlertFlags.Allergy | PetAlertFlags.Aggressive);
        pet.AlertNote.Should().Be("Penisiline alerji");
        PetAlertFlagNames.ToNames(pet.AlertFlags).Should().Equal("Allergy", "Aggressive");
    }

    [Fact]
    public void ApplyAlerts_Null_Arguments_Should_Leave_Existing_Values_Untouched()
    {
        var pet = CreatePet();
        pet.ApplyAlerts(["ChronicCondition"], "Diyabet");

        var result = pet.ApplyAlerts(null, null);

        result.IsSuccess.Should().BeTrue();
        pet.AlertFlags.Should().Be(PetAlertFlags.ChronicCondition);
        pet.AlertNote.Should().Be("Diyabet");
    }

    [Fact]
    public void ApplyAlerts_Only_Note_Should_Replace_Or_Clear_Note_Keeping_Flags()
    {
        var pet = CreatePet();
        pet.ApplyAlerts(["Allergy"], "Eski");

        pet.ApplyAlerts(null, "Yeni").IsSuccess.Should().BeTrue();
        pet.AlertNote.Should().Be("Yeni");

        pet.ApplyAlerts(null, "  ").IsSuccess.Should().BeTrue();
        pet.AlertNote.Should().BeNull();
        pet.AlertFlags.Should().Be(PetAlertFlags.Allergy);
    }

    [Fact]
    public void ApplyAlerts_Empty_List_Should_Clear_Flags_And_Note()
    {
        var pet = CreatePet();
        pet.ApplyAlerts(["Allergy", "AnesthesiaRisk"], "Not");

        var result = pet.ApplyAlerts([], null);

        result.IsSuccess.Should().BeTrue();
        pet.AlertFlags.Should().Be(PetAlertFlags.None);
        pet.AlertNote.Should().BeNull("bayraksız not tutulmaz");
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Unknown")]
    [InlineData("1")]
    [InlineData("Allergy, Aggressive")]
    [InlineData("")]
    public void ApplyAlerts_Should_Reject_Unknown_Flag_Names_Without_Changing_State(string name)
    {
        var pet = CreatePet();
        pet.ApplyAlerts(["Allergy"], "Not");

        var result = pet.ApplyAlerts([name], null);

        result.Error.Code.Should().Be("Pets.Validation");
        pet.AlertFlags.Should().Be(PetAlertFlags.Allergy);
        pet.AlertNote.Should().Be("Not");
    }

    [Fact]
    public void ApplyAlerts_Should_Reject_Note_Without_Flags_And_Too_Long_Note()
    {
        var pet = CreatePet();

        pet.ApplyAlerts(null, "Not").Error.Code.Should().Be("Pets.Validation");
        pet.ApplyAlerts([], "Not").Error.Code.Should().Be("Pets.Validation");
        pet.ApplyAlerts(["Allergy"], new string('x', Pet.MaxAlertNoteLength + 1)).Error.Code.Should().Be("Pets.Validation");
        pet.ApplyAlerts(["Allergy"], new string('x', Pet.MaxAlertNoteLength)).IsSuccess.Should().BeTrue();
    }
}
