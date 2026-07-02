using Backend.Veteriner.Application.Pets.Commands.Create;
using Backend.Veteriner.Application.Pets.Commands.Create.Validators;
using Backend.Veteriner.Application.Pets.Commands.Update;
using Backend.Veteriner.Application.Pets.Commands.Update.Validators;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Pets.Validators;

public sealed class PetCommandValidatorsTests
{
    private readonly CreatePetCommandValidator _createValidator = new();
    private readonly UpdatePetCommandValidator _updateValidator = new();

    [Fact]
    public void Create_Should_Succeed_When_IdentityFieldsEmpty()
    {
        var cmd = new CreatePetCommand(Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat);

        var result = _createValidator.Validate(cmd);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Succeed_When_IdentityFieldsPopulated()
    {
        var cmd = new CreatePetCommand(
            Guid.NewGuid(),
            "Pamuk",
            TestSpeciesIds.Cat,
            Gender: PetGender.Female,
            MicrochipNumber: "982000123456789",
            PassportOrTagNumber: "TR-12345",
            SpecialProtocolNumber: "PROT-001",
            IsNeutered: true);

        var result = _createValidator.Validate(cmd);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("MicrochipNumber")]
    [InlineData("PassportOrTagNumber")]
    [InlineData("SpecialProtocolNumber")]
    public void Create_Should_Fail_When_IdentityFieldExceedsMaxLength(string fieldName)
    {
        var tooLong = new string('A', 51);
        var cmd = fieldName switch
        {
            "MicrochipNumber" => new CreatePetCommand(Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, MicrochipNumber: tooLong),
            "PassportOrTagNumber" => new CreatePetCommand(Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, PassportOrTagNumber: tooLong),
            "SpecialProtocolNumber" => new CreatePetCommand(Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, SpecialProtocolNumber: tooLong),
            _ => throw new InvalidOperationException()
        };

        var result = _createValidator.Validate(cmd);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("MicrochipNumber")]
    [InlineData("PassportOrTagNumber")]
    [InlineData("SpecialProtocolNumber")]
    public void Update_Should_Fail_When_IdentityFieldExceedsMaxLength(string fieldName)
    {
        var tooLong = new string('A', 51);
        var cmd = fieldName switch
        {
            "MicrochipNumber" => new UpdatePetCommand(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, MicrochipNumber: tooLong),
            "PassportOrTagNumber" => new UpdatePetCommand(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, PassportOrTagNumber: tooLong),
            "SpecialProtocolNumber" => new UpdatePetCommand(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, SpecialProtocolNumber: tooLong),
            _ => throw new InvalidOperationException()
        };

        var result = _updateValidator.Validate(cmd);

        result.IsValid.Should().BeFalse();
    }
}
