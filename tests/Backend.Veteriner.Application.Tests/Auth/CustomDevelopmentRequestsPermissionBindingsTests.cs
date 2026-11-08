using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>TIER-010: özel geliştirme talebi kiracı ile Vetinity arasındaki ticari bir istektir; yalnız Admin ve Owner açar.</summary>
public sealed class CustomDevelopmentRequestsPermissionBindingsTests
{
    [Fact]
    public void Catalog_Should_Define_Custom_Development_Permission()
        => PermissionCatalog.All.Single(p => p.Code == PermissionCatalog.CustomDevelopmentRequests.Manage).Group
            .Should().Be("CustomDevelopmentRequests");

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Owner", true)]
    [InlineData("ClinicAdmin", false)]
    [InlineData("Sekreter", false)]
    [InlineData("Veteriner", false)]
    public void Only_Admin_And_Owner_Should_Manage_Requests(string role, bool expected)
        => RolePermissionBindings.Map[role].Contains(PermissionCatalog.CustomDevelopmentRequests.Manage).Should().Be(expected);
}
