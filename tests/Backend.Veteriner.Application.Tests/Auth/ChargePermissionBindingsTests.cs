using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>ADR-012: Sekreter ve yönetici düzeyi ücret ekler; Veteriner ekleyemez; düzeltme yalnız yönetici düzeyi.</summary>
public sealed class ChargePermissionBindingsTests
{
    private static IReadOnlyList<string> Perms(string role) => RolePermissionBindings.Map[role];

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("ClinicAdmin")]
    [InlineData("Sekreter")]
    public void Role_Should_Create_And_Read_Charges(string role)
    {
        Perms(role).Should().Contain(PermissionCatalog.Charges.Create);
        Perms(role).Should().Contain(PermissionCatalog.Charges.Read);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("ClinicAdmin")]
    public void Admin_Level_Role_Should_Correct_Charges(string role)
        => Perms(role).Should().Contain(PermissionCatalog.Charges.Correct);

    [Fact]
    public void Sekreter_Should_Not_Correct_Charges()
        => Perms("Sekreter").Should().NotContain(PermissionCatalog.Charges.Correct);

    [Fact]
    public void Veteriner_Should_Only_Read_Charges()
    {
        Perms("Veteriner").Should().Contain(PermissionCatalog.Charges.Read);
        Perms("Veteriner").Should().NotContain(PermissionCatalog.Charges.Create);
        Perms("Veteriner").Should().NotContain(PermissionCatalog.Charges.Correct);
    }

    [Fact]
    public void Charge_Permissions_Should_Be_In_Catalog()
    {
        PermissionCatalog.Contains(PermissionCatalog.Charges.Read).Should().BeTrue();
        PermissionCatalog.Contains(PermissionCatalog.Charges.Create).Should().BeTrue();
        PermissionCatalog.Contains(PermissionCatalog.Charges.Correct).Should().BeTrue();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("ClinicAdmin")]
    public void Admin_Level_Role_Should_Correct_Payments(string role)
        => Perms(role).Should().Contain(PermissionCatalog.Payments.Correct);

    [Theory]
    [InlineData("Sekreter")]
    [InlineData("Veteriner")]
    public void Sekreter_And_Veteriner_Should_Not_Correct_Payments(string role)
        => Perms(role).Should().NotContain(PermissionCatalog.Payments.Correct);

    [Fact]
    public void Veteriner_Should_Not_Create_Payments()
        => Perms("Veteriner").Should().NotContain(PermissionCatalog.Payments.Create);
}
