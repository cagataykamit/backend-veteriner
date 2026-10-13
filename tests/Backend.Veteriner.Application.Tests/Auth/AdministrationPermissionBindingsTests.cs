using Backend.Veteriner.Application.Auth;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>Aşama 5 Dilim 2 (D8) — uygulama kaydı dar izinleri; manuel stok izninden bağımsızdır.</summary>
public sealed class AdministrationPermissionBindingsTests
{
    private static IReadOnlyList<string> Perms(string roleName)
    {
        RolePermissionBindings.Map.TryGetValue(roleName, out var perms)
            .Should().BeTrue($"'{roleName}' rolü tanımlı olmalı");
        return perms!;
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("ClinicAdmin")]
    public void Admin_Level_Roles_Should_Create_Read_And_Void_Administrations(string role)
    {
        var p = Perms(role);
        p.Should().Contain(PermissionCatalog.Administrations.Read);
        p.Should().Contain(PermissionCatalog.Administrations.Create);
        p.Should().Contain(PermissionCatalog.Administrations.Correct);
    }

    [Fact]
    public void Veteriner_Should_Create_And_Read_But_Not_Void_Administrations()
    {
        var p = Perms("Veteriner");
        p.Should().Contain(PermissionCatalog.Administrations.Read);
        p.Should().Contain(PermissionCatalog.Administrations.Create);
        p.Should().NotContain(PermissionCatalog.Administrations.Correct);
    }

    [Fact]
    public void Veteriner_Should_Administer_Without_Manual_Stock_Create()
    {
        Perms("Veteriner").Should().NotContain(PermissionCatalog.StockMovements.Create);
    }

    [Fact]
    public void Sekreter_Should_Only_Read_Administrations()
    {
        var p = Perms("Sekreter");
        p.Should().Contain(PermissionCatalog.Administrations.Read);
        p.Should().NotContain(PermissionCatalog.Administrations.Create);
        p.Should().NotContain(PermissionCatalog.Administrations.Correct);
    }

    [Fact]
    public void Catalog_Should_List_Administration_Codes()
    {
        var all = PermissionCatalog.AllCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        all.Should().Contain(PermissionCatalog.Administrations.Read);
        all.Should().Contain(PermissionCatalog.Administrations.Create);
        all.Should().Contain(PermissionCatalog.Administrations.Correct);
    }
}
