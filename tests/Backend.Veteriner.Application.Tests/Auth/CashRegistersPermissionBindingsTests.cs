using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>TIER-008: resepsiyon kasaya giriş/çıkış ve aktarım girer; kasa tanımı ve hareket iptali yönetici düzeyinde (ADR-012 K2).</summary>
public sealed class CashRegistersPermissionBindingsTests
{
    [Theory]
    [InlineData(PermissionCatalog.CashRegisters.Read, "CashRegisters")]
    [InlineData(PermissionCatalog.CashRegisters.Manage, "CashRegisters")]
    [InlineData(PermissionCatalog.CashMovements.Create, "CashMovements")]
    [InlineData(PermissionCatalog.CashMovements.Void, "CashMovements")]
    public void Catalog_Should_Define_Cash_Register_Permissions(string code, string group)
        => PermissionCatalog.All.Single(p => p.Code == code).Group.Should().Be(group);

    [Theory]
    [InlineData("Admin", true, true, true, true)]
    [InlineData("Owner", true, true, true, true)]
    [InlineData("ClinicAdmin", true, true, true, true)]
    [InlineData("Sekreter", true, false, true, false)]
    [InlineData("Veteriner", false, false, false, false)]
    public void Roles_Should_Follow_Cash_Handling_Duties(string role, bool read, bool manage, bool create, bool voidMovement)
    {
        var map = RolePermissionBindings.Map[role];

        map.Contains(PermissionCatalog.CashRegisters.Read).Should().Be(read);
        map.Contains(PermissionCatalog.CashRegisters.Manage).Should().Be(manage);
        map.Contains(PermissionCatalog.CashMovements.Create).Should().Be(create);
        map.Contains(PermissionCatalog.CashMovements.Void).Should().Be(voidMovement);
    }
}
