using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Commands.QuickRegister;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Commands.Create;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using MediatR;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients;

public sealed class QuickRegisterClientCommandHandlerTests
{
    private readonly Mock<ISender> _sender = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _speciesId = Guid.NewGuid();

    private QuickRegisterClientCommandHandler CreateHandler() => new(_sender.Object);

    private QuickRegisterClientCommand Command() => new(
        "Ali Veli", "0555 111 22 33", "Pamuk", _speciesId,
        BreedId: Guid.NewGuid(), Breed: "Tekir", Gender: PetGender.Female,
        BirthDate: new DateOnly(2024, 5, 1), MicrochipNumber: "123456789012345", IsNeutered: true);

    private ClientCreatedDto SetupClientSuccess(Guid clientId)
    {
        var dto = new ClientCreatedDto(clientId, _tenantId, "Ali Veli", null, "905551112233");
        _sender.Setup(s => s.Send(It.IsAny<CreateClientCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClientCreatedDto>.Success(dto));
        return dto;
    }

    [Fact]
    public async Task Handle_Should_Create_Client_Then_Pet_And_Return_Both_Ids()
    {
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupClientSuccess(clientId);
        CreatePetCommand? petCommand = null;
        _sender.Setup(s => s.Send(It.IsAny<CreatePetCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<Guid>>, CancellationToken>((c, _) => petCommand = (CreatePetCommand)c)
            .ReturnsAsync(Result<Guid>.Success(petId));

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClientId.Should().Be(clientId);
        result.Value.PetId.Should().Be(petId);
        petCommand.Should().NotBeNull();
        petCommand!.ClientId.Should().Be(clientId);
        petCommand.Name.Should().Be("Pamuk");
        petCommand.SpeciesId.Should().Be(_speciesId);
        petCommand.Breed.Should().Be("Tekir");
        petCommand.Gender.Should().Be(PetGender.Female);
        petCommand.BirthDate.Should().Be(new DateOnly(2024, 5, 1));
        petCommand.MicrochipNumber.Should().Be("123456789012345");
        petCommand.IsNeutered.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Send_Client_Command_With_Name_And_Phone_Only()
    {
        SetupClientSuccess(Guid.NewGuid());
        CreateClientCommand? clientCommand = null;
        _sender.Setup(s => s.Send(It.IsAny<CreateClientCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<ClientCreatedDto>>, CancellationToken>((c, _) => clientCommand = (CreateClientCommand)c)
            .ReturnsAsync(Result<ClientCreatedDto>.Success(
                new ClientCreatedDto(Guid.NewGuid(), _tenantId, "Ali Veli", null, "905551112233")));
        _sender.Setup(s => s.Send(It.IsAny<CreatePetCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(Guid.NewGuid()));

        await CreateHandler().Handle(Command(), CancellationToken.None);

        clientCommand!.FullName.Should().Be("Ali Veli");
        clientCommand.Phone.Should().Be("0555 111 22 33");
        clientCommand.Email.Should().BeNull();
        clientCommand.Address.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_Return_Client_Failure_And_Not_Create_Pet()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateClientCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClientCreatedDto>.Failure("Clients.DuplicateClient", "mukerrer"));

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.DuplicateClient");
        _sender.Verify(s => s.Send(It.IsAny<CreatePetCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_Pet_Failure()
    {
        SetupClientSuccess(Guid.NewGuid());
        _sender.Setup(s => s.Send(It.IsAny<CreatePetCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("Pets.SpeciesNotFound", "yok"));

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.SpeciesNotFound");
    }

    [Fact]
    public void Command_Should_Be_Marked_To_Roll_Back_On_Failure()
        => Command().Should().BeAssignableTo<ITransactionalRollbackOnFailureRequest>();

    [Theory]
    [InlineData("", "0555 111 22 33", "Pamuk", "FullName")]
    [InlineData("Ali Veli", "", "Pamuk", "Phone")]
    [InlineData("Ali Veli", "   ", "Pamuk", "Phone")]
    [InlineData("Ali Veli", "0555 111 22 33", "", "PetName")]
    public void Validator_Should_Require_Name_Phone_And_PetName(string name, string phone, string pet, string property)
    {
        var result = new QuickRegisterClientCommandValidator().Validate(
            new QuickRegisterClientCommand(name, phone, pet, _speciesId));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == property);
    }

    [Fact]
    public void Validator_Should_Require_Species_And_Accept_Complete_Request()
    {
        var validator = new QuickRegisterClientCommandValidator();

        validator.Validate(new QuickRegisterClientCommand("Ali Veli", "0555 111 22 33", "Pamuk", Guid.Empty))
            .Errors.Should().Contain(e => e.PropertyName == "SpeciesId");
        validator.Validate(Command()).IsValid.Should().BeTrue();
    }
}
