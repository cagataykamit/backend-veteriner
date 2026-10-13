using Backend.Veteriner.Application.Public.Commands.OwnerSignup;
using Backend.Veteriner.Application.Public.Commands.OwnerSignup.Validators;
using Backend.Veteriner.Application.Tenants.Commands.Create;
using Backend.Veteriner.Application.Tenants.Commands.Create.Validators;
using Backend.Veteriner.Application.Tenants.Commands.UpdateSettings;
using Backend.Veteriner.Application.Tenants.Commands.UpdateSettings.Validators;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Tenants.Common;

public sealed class ReservedTenantNameValidationTests
{
    [Theory]
    [InlineData("Platform")]
    [InlineData("platform")]
    [InlineData("  PLATFORM ")]
    public void CreateTenant_Should_Reject_ReservedName(string name)
        => new CreateTenantCommandValidator().Validate(new CreateTenantCommand(name)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("Platform")]
    [InlineData("platform")]
    public void UpdateSettings_Should_Reject_ReservedName(string name)
        => new UpdateTenantSettingsCommandValidator()
            .Validate(new UpdateTenantSettingsCommand(Guid.NewGuid(), name)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("Platform")]
    [InlineData("PLATFORM")]
    public void OwnerSignup_Should_Reject_ReservedName(string name)
    {
        var result = new PublicOwnerSignupCommandValidator().Validate(
            new PublicOwnerSignupCommand("Basic", name, "Klinik", "Istanbul", "owner@example.com", "Guclu-Parola-9!"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(PublicOwnerSignupCommand.TenantName));
    }

    [Fact]
    public void CreateTenant_Should_Accept_NameContainingPlatform()
        => new CreateTenantCommandValidator().Validate(new CreateTenantCommand("Platform Veteriner Kliniği")).IsValid.Should().BeTrue();

    [Fact]
    public void IsReservedName_Should_MatchPlatformTenantName_CaseInsensitively()
    {
        Tenant.IsReservedName(Tenant.PlatformTenantName).Should().BeTrue();
        Tenant.IsReservedName(" platform ").Should().BeTrue();
        Tenant.IsReservedName("Varsayılan Kiracı").Should().BeFalse();
        Tenant.IsReservedName(null).Should().BeFalse();
    }
}
