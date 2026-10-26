using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>Görüntüleme izin matrisi (IMAGING_API_CONTRACT.md §3): yönetim + Veteriner tam, Sekreter yalnız okur.</summary>
public sealed class ImagingPermissionBindingsTests
{
    private static readonly string[] All =
    [
        PermissionCatalog.Imaging.Read,
        PermissionCatalog.Imaging.Create,
        PermissionCatalog.Imaging.Update,
        PermissionCatalog.Imaging.Cancel
    ];

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
    [InlineData("Veteriner")]
    public void Clinical_And_Admin_Roles_Should_Have_All_Imaging_Permissions(string role)
        => Perms(role).Should().Contain(All);

    [Fact]
    public void Sekreter_Should_Only_Read_Imaging()
    {
        var p = Perms("Sekreter");
        p.Should().Contain(PermissionCatalog.Imaging.Read);
        p.Should().NotContain(PermissionCatalog.Imaging.Create);
        p.Should().NotContain(PermissionCatalog.Imaging.Update);
        p.Should().NotContain(PermissionCatalog.Imaging.Cancel);
    }

    [Fact]
    public void Catalog_Should_List_Imaging_Codes()
        => PermissionCatalog.AllCodes.Should().Contain(All);
}
