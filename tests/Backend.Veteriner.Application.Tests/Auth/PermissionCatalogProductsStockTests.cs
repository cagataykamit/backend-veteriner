using Backend.Veteriner.Application.Auth;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>Faz 10B-1 — Ürün/kategori/stok hareketi permission katalogu.</summary>
public sealed class PermissionCatalogProductsStockTests
{
    public static readonly TheoryData<string> ProductCategoryPermissionCodes = new()
    {
        PermissionCatalog.ProductCategories.Read,
        PermissionCatalog.ProductCategories.Create,
        PermissionCatalog.ProductCategories.Update,
        PermissionCatalog.ProductCategories.Deactivate
    };

    public static readonly TheoryData<string> ProductPermissionCodes = new()
    {
        PermissionCatalog.Products.Read,
        PermissionCatalog.Products.Create,
        PermissionCatalog.Products.Update,
        PermissionCatalog.Products.Deactivate
    };

    public static readonly TheoryData<string> StockMovementPermissionCodes = new()
    {
        PermissionCatalog.StockMovements.Read,
        PermissionCatalog.StockMovements.Create
    };

    [Theory]
    [MemberData(nameof(ProductCategoryPermissionCodes))]
    public void PermissionCatalog_Should_Contain_ProductCategory_Code(string code)
    {
        PermissionCatalog.Contains(code).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ProductPermissionCodes))]
    public void PermissionCatalog_Should_Contain_Product_Code(string code)
    {
        PermissionCatalog.Contains(code).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(StockMovementPermissionCodes))]
    public void PermissionCatalog_Should_Contain_StockMovement_Code(string code)
    {
        PermissionCatalog.Contains(code).Should().BeTrue();
    }

    [Fact]
    public void PermissionCatalog_All_Should_Include_All_Inventory_Codes()
    {
        var union = new[]
            {
                PermissionCatalog.ProductCategories.Read,
                PermissionCatalog.ProductCategories.Create,
                PermissionCatalog.ProductCategories.Update,
                PermissionCatalog.ProductCategories.Deactivate,
                PermissionCatalog.Products.Read,
                PermissionCatalog.Products.Create,
                PermissionCatalog.Products.Update,
                PermissionCatalog.Products.Deactivate,
                PermissionCatalog.StockMovements.Read,
                PermissionCatalog.StockMovements.Create
            }
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var allCodes = PermissionCatalog.All.Select(a => a.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        union.Should().OnlyContain(c => allCodes.Contains(c));
    }

    [Fact]
    public void PermissionCatalog_Should_Not_Define_Disallowed_Product_Or_Stock_Codes_Or_Inventory_Manage()
    {
        PermissionCatalog.Contains("Products.Delete").Should().BeFalse();
        PermissionCatalog.Contains("StockMovements.Update").Should().BeFalse();
        PermissionCatalog.Contains("StockMovements.Delete").Should().BeFalse();
        PermissionCatalog.Contains("Inventory.Manage").Should().BeFalse();
        PermissionCatalog.Contains("Inventory.Read").Should().BeFalse();
    }

    [Fact]
    public void PermissionCatalog_All_Definitions_Should_Have_Text_For_Inventory_Codes()
    {
        foreach (var code in new[]
                     {
                         PermissionCatalog.ProductCategories.Read,
                         PermissionCatalog.ProductCategories.Create,
                         PermissionCatalog.ProductCategories.Update,
                         PermissionCatalog.ProductCategories.Deactivate,
                         PermissionCatalog.Products.Read,
                         PermissionCatalog.Products.Create,
                         PermissionCatalog.Products.Update,
                         PermissionCatalog.Products.Deactivate,
                         PermissionCatalog.StockMovements.Read,
                         PermissionCatalog.StockMovements.Create
                     })
        {
            var entry = PermissionCatalog.All.Single(p =>
                string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

            entry.Description.Should().NotBeNullOrWhiteSpace();
            entry.Group.Should().NotBeNullOrWhiteSpace();
        }
    }
}
