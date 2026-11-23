using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>ADR-054 K8: müşteri/hasta silme, "Silinenler" ve geri yükleme yönetici düzeyinde.</summary>
public sealed class ClientPetDeletePermissionBindingsTests
{
    [Theory]
    [InlineData(PermissionCatalog.Clients.Delete, "Clients")]
    [InlineData(PermissionCatalog.Pets.Delete, "Pets")]
    public void Catalog_Should_Define_Delete_Permissions(string code, string group)
        => PermissionCatalog.All.Single(p => p.Code == code).Group.Should().Be(group);

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Owner", true)]
    [InlineData("ClinicAdmin", true)]
    [InlineData("Veteriner", false)]
    [InlineData("Sekreter", false)]
    public void Only_Managers_Should_Delete_And_Restore(string role, bool granted)
    {
        var map = RolePermissionBindings.Map[role];

        map.Contains(PermissionCatalog.Clients.Delete).Should().Be(granted);
        map.Contains(PermissionCatalog.Pets.Delete).Should().Be(granted);
    }
}
