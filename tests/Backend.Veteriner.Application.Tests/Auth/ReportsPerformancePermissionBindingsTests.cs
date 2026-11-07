using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>TIER-004 / UX taslağı S1: hekim bazlı rapor yalnız yönetici rollerinde.</summary>
public sealed class ReportsPerformancePermissionBindingsTests
{
    [Fact]
    public void Catalog_Should_Define_Reports_Performance()
        => PermissionCatalog.All.Single(p => p.Code == PermissionCatalog.Reports.Performance).Group.Should().Be("Reports");

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Owner", true)]
    [InlineData("ClinicAdmin", true)]
    [InlineData("Veteriner", false)]
    [InlineData("Sekreter", false)]
    public void Only_Manager_Roles_Should_See_Person_Performance(string role, bool expected)
        => RolePermissionBindings.Map[role].Contains(PermissionCatalog.Reports.Performance).Should().Be(expected);
}
