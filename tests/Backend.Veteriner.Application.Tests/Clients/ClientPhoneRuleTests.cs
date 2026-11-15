using Backend.Veteriner.Application.Clients;
using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Commands.Create.Validators;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Clients;

/// <summary>H17 (2026-11-15): yeni müşteride telefon zorunlu; içe aktarma muaf; hata metni örnek biçim verir.</summary>
public sealed class ClientPhoneRuleTests
{
    private readonly CreateClientCommandValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Require_Phone(string? phone)
    {
        var result = _validator.Validate(new CreateClientCommand("Ali Veli", Phone: phone));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateClientCommand.Phone))
            .Which.ErrorMessage.Should().Be(ClientPhoneRule.RequiredMessage);
    }

    [Fact]
    public void Create_From_Import_Should_Allow_Missing_Phone()
        => _validator.Validate(new CreateClientCommand("Ali Veli") { FromImport = true }).IsValid.Should().BeTrue();

    [Fact]
    public void Create_Should_Give_Example_For_Invalid_Phone()
        => _validator.Validate(new CreateClientCommand("Ali Veli", Phone: "12345")).Errors
            .Should().ContainSingle().Which.ErrorMessage.Should().Contain("0532 111 22 33");

    [Fact]
    public void Create_Should_Accept_Valid_Phone()
        => _validator.Validate(new CreateClientCommand("Ali Veli", Phone: "0532 111 22 33")).IsValid.Should().BeTrue();
}
