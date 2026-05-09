using System.Reflection;
using Backend.Veteriner.Api.Controllers;
using Backend.Veteriner.Application.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace Backend.IntegrationTests.Products;

public sealed class ProductInventoryPolicyMappingTests
{
    [Fact]
    public void ProductCategoriesController_Policies_Should_Match_Contract()
    {
        GetPolicy<ProductCategoriesController>(nameof(ProductCategoriesController.GetList))
            .Should().Be(PermissionCatalog.ProductCategories.Read);
        GetPolicy<ProductCategoriesController>(nameof(ProductCategoriesController.GetById))
            .Should().Be(PermissionCatalog.ProductCategories.Read);
        GetPolicy<ProductCategoriesController>(nameof(ProductCategoriesController.Create))
            .Should().Be(PermissionCatalog.ProductCategories.Create);
        GetPolicy<ProductCategoriesController>(nameof(ProductCategoriesController.Update))
            .Should().Be(PermissionCatalog.ProductCategories.Update);
        GetPolicy<ProductCategoriesController>(nameof(ProductCategoriesController.Activate))
            .Should().Be(PermissionCatalog.ProductCategories.Update);
        GetPolicy<ProductCategoriesController>(nameof(ProductCategoriesController.Deactivate))
            .Should().Be(PermissionCatalog.ProductCategories.Deactivate);
    }

    [Fact]
    public void ProductsController_Policies_Should_Match_Contract()
    {
        GetPolicy<ProductsController>(nameof(ProductsController.GetList))
            .Should().Be(PermissionCatalog.Products.Read);
        GetPolicy<ProductsController>(nameof(ProductsController.GetById))
            .Should().Be(PermissionCatalog.Products.Read);
        GetPolicy<ProductsController>(nameof(ProductsController.Create))
            .Should().Be(PermissionCatalog.Products.Create);
        GetPolicy<ProductsController>(nameof(ProductsController.Update))
            .Should().Be(PermissionCatalog.Products.Update);
        GetPolicy<ProductsController>(nameof(ProductsController.Activate))
            .Should().Be(PermissionCatalog.Products.Update);
        GetPolicy<ProductsController>(nameof(ProductsController.Deactivate))
            .Should().Be(PermissionCatalog.Products.Deactivate);
        GetPolicy<ProductsController>(nameof(ProductsController.GetStocksByProductId))
            .Should().Be(PermissionCatalog.Products.Read);
        GetPolicy<ProductsController>(nameof(ProductsController.GetStockMovementsByProductId))
            .Should().Be(PermissionCatalog.StockMovements.Read);
    }

    [Fact]
    public void ProductStocksController_Policies_Should_Match_Contract()
    {
        GetPolicy<ProductStocksController>(nameof(ProductStocksController.GetList))
            .Should().Be(PermissionCatalog.Products.Read);
    }

    [Fact]
    public void StockMovementsController_Policies_Should_Match_Contract()
    {
        GetPolicy<StockMovementsController>(nameof(StockMovementsController.GetList))
            .Should().Be(PermissionCatalog.StockMovements.Read);
    }

    private static string? GetPolicy<TController>(string actionName)
    {
        var method = typeof(TController).GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull($"'{actionName}' action'ı bulunmalı");
        var authorize = method!.GetCustomAttributes<AuthorizeAttribute>(inherit: false).FirstOrDefault();
        authorize.Should().NotBeNull($"'{actionName}' action'ında [Authorize] olmalı");
        return authorize!.Policy;
    }
}
