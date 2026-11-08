using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>TIER-009: API anahtarı kiracının tüm müşteri verisini okur; yalnız Admin ve Owner yönetir (en dar yetki).</summary>
public sealed class ApiKeysPermissionBindingsTests
{
    [Fact]
    public void Catalog_Should_Define_Api_Key_Permission()
        => PermissionCatalog.All.Single(p => p.Code == PermissionCatalog.ApiKeys.Manage).Group.Should().Be("ApiKeys");

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Owner", true)]
    [InlineData("ClinicAdmin", false)]
    [InlineData("Sekreter", false)]
    [InlineData("Veteriner", false)]
    public void Only_Admin_And_Owner_Should_Manage_Api_Keys(string role, bool expected)
        => RolePermissionBindings.Map[role].Contains(PermissionCatalog.ApiKeys.Manage).Should().Be(expected);
}
