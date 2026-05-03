using Backend.Veteriner.Application.Clinics.Commands.Create;
using Backend.Veteriner.Application.Clinics.Commands.Create.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Clinics.Validators;

public sealed class CreateClinicCommandValidatorTests
{
    private readonly CreateClinicCommandValidator _validator = new();

    [Fact]
    public void Should_Fail_When_EmailInvalid()
    {
        var r = _validator.Validate(new CreateClinicCommand("Merkez", "İstanbul", Email: "not-an-email"));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_PhoneTooLong()
    {
        var r = _validator.Validate(new CreateClinicCommand("Merkez", "İstanbul", Phone: new string('1', 51)));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Pass_When_OptionalFieldsNullOrValid()
    {
        _validator.Validate(new CreateClinicCommand("Merkez", "İstanbul")).IsValid.Should().BeTrue();
        _validator.Validate(new CreateClinicCommand("Merkez", "İstanbul", Email: "ok@example.com")).IsValid.Should().BeTrue();
    }
}
