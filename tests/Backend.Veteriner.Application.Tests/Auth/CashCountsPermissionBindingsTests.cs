using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>TIER-008: kasa sayımını resepsiyon ve yöneticiler kaydeder, yalnız yöneticiler düzeltir; hekim kasa sayımı görmez.</summary>
public sealed class CashCountsPermissionBindingsTests
{
    [Theory]
    [InlineData(PermissionCatalog.CashCounts.Read)]
    [InlineData(PermissionCatalog.CashCounts.Create)]
    [InlineData(PermissionCatalog.CashCounts.Correct)]
    public void Catalog_Should_Define_Cash_Count_Permissions(string code)
        => PermissionCatalog.All.Single(p => p.Code == code).Group.Should().Be("CashCounts");

    [Theory]
    [InlineData("Admin", true, true, true)]
    [InlineData("Owner", true, true, true)]
    [InlineData("ClinicAdmin", true, true, true)]
    [InlineData("Sekreter", true, true, false)]
    [InlineData("Veteriner", false, false, false)]
    public void Roles_Should_Follow_Cash_Handling_Duties(string role, bool read, bool create, bool correct)
    {
        var map = RolePermissionBindings.Map[role];

        map.Contains(PermissionCatalog.CashCounts.Read).Should().Be(read);
        map.Contains(PermissionCatalog.CashCounts.Create).Should().Be(create);
        map.Contains(PermissionCatalog.CashCounts.Correct).Should().Be(correct);
    }
}
