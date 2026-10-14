using Backend.Veteriner.Application.Auth;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>Doğrudan satış izinleri (ADR-016 karar 9): satış sekreter + yönetici düzeyi, iptal yalnız yönetici düzeyi, Veteriner yok.</summary>
public sealed class SalePermissionBindingsTests
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
    public void Admin_Level_Roles_Should_Read_Create_And_Void_Sales(string role)
    {
        var p = Perms(role);
        p.Should().Contain(PermissionCatalog.Sales.Read);
        p.Should().Contain(PermissionCatalog.Sales.Create);
        p.Should().Contain(PermissionCatalog.Sales.Correct);
    }

    [Fact]
    public void Sekreter_Should_Read_And_Create_But_Not_Void_Sales()
    {
        var p = Perms("Sekreter");
        p.Should().Contain(PermissionCatalog.Sales.Read);
        p.Should().Contain(PermissionCatalog.Sales.Create);
        p.Should().NotContain(PermissionCatalog.Sales.Correct);
    }

    [Fact]
    public void Veteriner_Should_Have_No_Sales_Permissions()
    {
        Perms("Veteriner").Should().NotContain(c => c.StartsWith("Sales.", StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_Should_List_Sales_Codes()
    {
        var all = PermissionCatalog.AllCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        all.Should().Contain(PermissionCatalog.Sales.Read);
        all.Should().Contain(PermissionCatalog.Sales.Create);
        all.Should().Contain(PermissionCatalog.Sales.Correct);
    }
}
