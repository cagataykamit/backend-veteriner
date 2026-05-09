using Backend.Veteriner.Application.Auth;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>Faz 10B-1 — Ürün/stok varsayılan rol matrisi.</summary>
public sealed class RolePermissionBindingsProductsStockTests
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
    public void Operational_Admin_Roles_Should_Have_Full_Inventory_Permissions(string role)
    {
        var p = Perms(role);
        p.Should().Contain(PermissionCatalog.ProductCategories.Read);
        p.Should().Contain(PermissionCatalog.ProductCategories.Create);
        p.Should().Contain(PermissionCatalog.ProductCategories.Update);
        p.Should().Contain(PermissionCatalog.ProductCategories.Deactivate);

        p.Should().Contain(PermissionCatalog.Products.Read);
        p.Should().Contain(PermissionCatalog.Products.Create);
        p.Should().Contain(PermissionCatalog.Products.Update);
        p.Should().Contain(PermissionCatalog.Products.Deactivate);

        p.Should().Contain(PermissionCatalog.StockMovements.Read);
        p.Should().Contain(PermissionCatalog.StockMovements.Create);
    }

    [Fact]
    public void Sekreter_Should_Have_Read_And_Stock_Create_Only_For_Inventory()
    {
        var p = Perms("Sekreter");
        p.Should().Contain(PermissionCatalog.ProductCategories.Read);
        p.Should().Contain(PermissionCatalog.Products.Read);
        p.Should().Contain(PermissionCatalog.StockMovements.Read);
        p.Should().Contain(PermissionCatalog.StockMovements.Create);
    }

    [Fact]
    public void Sekreter_Should_Not_Have_Product_Or_Category_Mutation_Permissions()
    {
        var p = Perms("Sekreter");
        p.Should().NotContain(PermissionCatalog.Products.Create);
        p.Should().NotContain(PermissionCatalog.Products.Update);
        p.Should().NotContain(PermissionCatalog.Products.Deactivate);
        p.Should().NotContain(PermissionCatalog.ProductCategories.Create);
        p.Should().NotContain(PermissionCatalog.ProductCategories.Update);
        p.Should().NotContain(PermissionCatalog.ProductCategories.Deactivate);
    }

    [Fact]
    public void Veteriner_Should_Have_Read_Only_Inventory_Permissions()
    {
        var p = Perms("Veteriner");
        p.Should().Contain(PermissionCatalog.ProductCategories.Read);
        p.Should().Contain(PermissionCatalog.Products.Read);
        p.Should().Contain(PermissionCatalog.StockMovements.Read);
    }

    [Fact]
    public void Veteriner_Should_Not_Have_Product_Category_Or_Stock_Create_Permissions()
    {
        var p = Perms("Veteriner");
        p.Should().NotContain(PermissionCatalog.Products.Create);
        p.Should().NotContain(PermissionCatalog.Products.Update);
        p.Should().NotContain(PermissionCatalog.Products.Deactivate);
        p.Should().NotContain(PermissionCatalog.ProductCategories.Create);
        p.Should().NotContain(PermissionCatalog.ProductCategories.Update);
        p.Should().NotContain(PermissionCatalog.ProductCategories.Deactivate);
        p.Should().NotContain(PermissionCatalog.StockMovements.Create);
    }

    [Fact]
    public void PlatformAdmin_Is_Not_In_Map_But_Catalog_All_Includes_Inventory_Codes()
    {
        RolePermissionBindings.Map.ContainsKey("PlatformAdmin").Should().BeFalse();

        var all = PermissionCatalog.AllCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        all.Should().Contain(PermissionCatalog.Products.Read);
        all.Should().Contain(PermissionCatalog.StockMovements.Create);
    }
}
