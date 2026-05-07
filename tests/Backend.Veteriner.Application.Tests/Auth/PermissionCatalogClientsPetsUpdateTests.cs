using Backend.Veteriner.Application.Auth;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>
/// Faz 4B-4 — Clients.Update / Pets.Update permission düzeltmesi.
/// PermissionCatalog yeni Update kodlarını içermeli ve ClinicAdmin (Create yetkisi alan default rol)
/// geriye uyum için Update kodlarını da almalı. Platform Admin tüm permission’ları
/// AdminClaimSeeder üzerinden otomatik aldığı için Map["Admin"]/Map["Owner"] minimumu burada test edilmez.
/// </summary>
public sealed class PermissionCatalogClientsPetsUpdateTests
{
    [Theory]
    [InlineData("Clients.Update")]
    [InlineData("Pets.Update")]
    public void PermissionCatalog_Should_Contain_NewUpdateCode(string code)
    {
        PermissionCatalog.Contains(code).Should().BeTrue($"PermissionCatalog '{code}' kodunu içermeli");
    }

    [Theory]
    [InlineData("Clients.Update")]
    [InlineData("Pets.Update")]
    public void PermissionCatalog_All_Should_Have_Definition_For_NewUpdateCode(string code)
    {
        var entry = PermissionCatalog.All.SingleOrDefault(p =>
            string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

        entry.Should().NotBeNull($"PermissionCatalog.All içinde '{code}' tanımı olmalı");
        entry!.Description.Should().NotBeNullOrWhiteSpace();
        entry.Group.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PermissionCatalog_Constants_Should_Match_Expected_Codes()
    {
        PermissionCatalog.Clients.Update.Should().Be("Clients.Update");
        PermissionCatalog.Pets.Update.Should().Be("Pets.Update");
    }

    [Theory]
    [InlineData("Clients.Update")]
    [InlineData("Pets.Update")]
    public void RolePermissionBindings_ClinicAdmin_Should_Include_NewUpdatePermission(string code)
    {
        RolePermissionBindings.Map.TryGetValue("ClinicAdmin", out var permissions)
            .Should().BeTrue("ClinicAdmin map girişi olmalı");

        permissions!.Should().Contain(code,
            $"ClinicAdmin rolü Create yetkisini aldığı için geriye uyum gereği '{code}' yetkisini de almalı");
    }

    [Fact]
    public void RolePermissionBindings_ClinicAdmin_Should_Still_Include_CreatePermissions()
    {
        var perms = RolePermissionBindings.Map["ClinicAdmin"];
        perms.Should().Contain(PermissionCatalog.Clients.Create);
        perms.Should().Contain(PermissionCatalog.Pets.Create);
    }
}
