using Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Add;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth.Claims.Validators;

public sealed class AddPermissionToClaimCommandValidatorTests
{
    private readonly AddPermissionToClaimCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_OperationClaimId_IsEmpty()
    {
        var cmd = new AddPermissionToClaimCommand(Guid.Empty, Guid.NewGuid());
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AddPermissionToClaimCommand.OperationClaimId));
    }

    [Fact]
    public void Validate_Should_Fail_When_PermissionId_IsEmpty()
    {
        var cmd = new AddPermissionToClaimCommand(Guid.NewGuid(), Guid.Empty);
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AddPermissionToClaimCommand.PermissionId));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        var cmd = new AddPermissionToClaimCommand(Guid.NewGuid(), Guid.NewGuid());
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeTrue();
    }
}
