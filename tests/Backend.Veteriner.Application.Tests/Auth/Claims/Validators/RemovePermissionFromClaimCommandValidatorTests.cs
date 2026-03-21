using Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Remove;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth.Claims.Validators;

public sealed class RemovePermissionFromClaimCommandValidatorTests
{
    private readonly RemovePermissionFromClaimCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_OperationClaimId_IsEmpty()
    {
        var cmd = new RemovePermissionFromClaimCommand(Guid.Empty, Guid.NewGuid());
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RemovePermissionFromClaimCommand.OperationClaimId));
    }

    [Fact]
    public void Validate_Should_Fail_When_PermissionId_IsEmpty()
    {
        var cmd = new RemovePermissionFromClaimCommand(Guid.NewGuid(), Guid.Empty);
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RemovePermissionFromClaimCommand.PermissionId));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        var cmd = new RemovePermissionFromClaimCommand(Guid.NewGuid(), Guid.NewGuid());
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeTrue();
    }
}
