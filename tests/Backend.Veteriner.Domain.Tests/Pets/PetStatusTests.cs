using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Pets;

public sealed class PetStatusTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static Pet CreatePet(DateOnly? birthDate = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", Guid.NewGuid(), birthDate: birthDate);

    [Fact]
    public void New_Pet_Should_Be_Active_And_Receive_Care()
    {
        var pet = CreatePet();

        pet.Status.Should().Be(PetStatus.Active);
        pet.DeceasedOn.Should().BeNull();
        pet.EnsureCanStartCare().IsSuccess.Should().BeTrue();
        Pet.ReceivesCare.Compile()(pet).Should().BeTrue();
    }

    [Fact]
    public void SetStatus_Deceased_Should_Set_Optional_Date_And_Block_New_Care()
    {
        var pet = CreatePet();

        pet.SetStatus(PetStatus.Deceased, Today.AddDays(-2), Today).IsSuccess.Should().BeTrue();

        pet.Status.Should().Be(PetStatus.Deceased);
        pet.DeceasedOn.Should().Be(Today.AddDays(-2));
        Pet.ReceivesCare.Compile()(pet).Should().BeFalse();
        var gate = pet.EnsureCanStartCare();
        gate.IsSuccess.Should().BeFalse();
        gate.Error.Code.Should().Be("Pets.Deceased");
    }

    [Fact]
    public void SetStatus_Deceased_Without_Date_Should_Succeed()
    {
        var pet = CreatePet();

        pet.SetStatus(PetStatus.Deceased, null, Today).IsSuccess.Should().BeTrue();

        pet.Status.Should().Be(PetStatus.Deceased);
        pet.DeceasedOn.Should().BeNull();
    }

    [Fact]
    public void SetStatus_Deceased_Should_Reject_Future_Date()
    {
        var pet = CreatePet();

        var result = pet.SetStatus(PetStatus.Deceased, Today.AddDays(1), Today);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.Validation");
        pet.Status.Should().Be(PetStatus.Active);
    }

    [Fact]
    public void SetStatus_Deceased_Should_Reject_Date_Before_Birth()
    {
        var pet = CreatePet(birthDate: Today.AddYears(-1));

        var result = pet.SetStatus(PetStatus.Deceased, Today.AddYears(-2), Today);

        result.IsSuccess.Should().BeFalse();
        pet.Status.Should().Be(PetStatus.Active);
    }

    [Fact]
    public void SetStatus_Deceased_Again_Should_Correct_Date()
    {
        var pet = CreatePet();
        pet.SetStatus(PetStatus.Deceased, Today.AddDays(-5), Today);

        pet.SetStatus(PetStatus.Deceased, Today.AddDays(-3), Today).IsSuccess.Should().BeTrue();

        pet.DeceasedOn.Should().Be(Today.AddDays(-3));
    }

    [Fact]
    public void SetStatus_Active_Should_Revert_And_Clear_Date()
    {
        var pet = CreatePet();
        pet.SetStatus(PetStatus.Deceased, Today, Today);

        pet.SetStatus(PetStatus.Active, null, Today).IsSuccess.Should().BeTrue();

        pet.Status.Should().Be(PetStatus.Active);
        pet.DeceasedOn.Should().BeNull();
        pet.EnsureCanStartCare().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void SetStatus_Active_With_Date_Should_Fail()
    {
        var pet = CreatePet();
        pet.SetStatus(PetStatus.Deceased, Today, Today);

        var result = pet.SetStatus(PetStatus.Active, Today, Today);

        result.IsSuccess.Should().BeFalse();
        pet.Status.Should().Be(PetStatus.Deceased);
    }

    [Fact]
    public void SetStatus_Undefined_Should_Fail()
    {
        var pet = CreatePet();

        pet.SetStatus((PetStatus)0, null, Today).IsSuccess.Should().BeFalse();
        pet.Status.Should().Be(PetStatus.Active);
    }
}
