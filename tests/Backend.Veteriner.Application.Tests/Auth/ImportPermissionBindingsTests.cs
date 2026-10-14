using Backend.Veteriner.Application.Auth;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>ADR-019 K8: içe aktarma izinleri yalnız yönetici düzeyinde (Admin, Owner, ClinicAdmin).</summary>
public sealed class ImportPermissionBindingsTests
{
    private static readonly string[] ImportPermissions =
    [
        PermissionCatalog.Imports.Read,
        PermissionCatalog.Imports.Manage,
        PermissionCatalog.Imports.Commit,
    ];

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("ClinicAdmin")]
    public void Admin_Level_Role_Should_Have_All_Import_Permissions(string role)
        => RolePermissionBindings.Map[role].Should().Contain(ImportPermissions);

    [Theory]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public void Operational_Role_Should_Have_No_Import_Permission(string role)
        => RolePermissionBindings.Map[role].Should().NotContain(ImportPermissions);

    [Fact]
    public void Import_Permissions_Should_Be_In_Catalog_Under_Imports_Group()
    {
        foreach (var code in ImportPermissions)
            PermissionCatalog.All.Single(p => p.Code == code).Group.Should().Be("Imports");
    }
}
