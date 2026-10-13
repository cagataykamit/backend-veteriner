using Backend.Veteriner.Application.Auth;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>
/// ADR-012 yönetici düzeyi (Admin, Owner, ClinicAdmin): tüm düzeltme izinleri (<c>*.Correct</c>) yalnız bu rollerde olmalıdır.
/// Kural tek yerde (<see cref="RolePermissionBindings"/>); bu matris izinlerin tutarlı dağıldığını gösterir.
/// </summary>
public sealed class CorrectionPermissionMatrixTests
{
    private static readonly string[] AdminLevelRoles = ["Admin", "Owner", "ClinicAdmin"];
    private static readonly string[] NonAdminRoles = ["Veteriner", "Sekreter"];

    public static readonly TheoryData<string> CorrectionCodes = new()
    {
        PermissionCatalog.Charges.Correct,
        PermissionCatalog.Payments.Correct,
        PermissionCatalog.Administrations.Correct,
        PermissionCatalog.StockMovements.Correct
    };

    private static IReadOnlyList<string> Perms(string roleName)
    {
        RolePermissionBindings.Map.TryGetValue(roleName, out var perms)
            .Should().BeTrue($"'{roleName}' rolü tanımlı olmalı");
        return perms!;
    }

    [Theory]
    [MemberData(nameof(CorrectionCodes))]
    public void Correction_Permission_Should_Be_In_Catalog_With_Description(string code)
    {
        PermissionCatalog.Contains(code).Should().BeTrue();
        var entry = PermissionCatalog.All.Single(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        entry.Description.Should().NotBeNullOrWhiteSpace();
        entry.Group.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(CorrectionCodes))]
    public void Correction_Permission_Should_Be_Granted_To_Admin_Level_Roles_Only(string code)
    {
        foreach (var role in AdminLevelRoles)
            Perms(role).Should().Contain(code, $"'{role}' yönetici düzeyindedir");

        foreach (var role in NonAdminRoles)
            Perms(role).Should().NotContain(code, $"'{role}' yönetici düzeyinde değildir");
    }

    [Fact]
    public void Sekreter_Can_Add_Stock_Movements_But_Cannot_Reverse_Them()
    {
        var p = Perms("Sekreter");
        p.Should().Contain(PermissionCatalog.StockMovements.Create);
        p.Should().NotContain(PermissionCatalog.StockMovements.Correct);
    }

    [Fact]
    public void Revoked_Bindings_Should_Not_Overlap_With_Defaults()
    {
        foreach (var (role, codes) in RolePermissionBindings.Revoked)
        {
            codes.Should().OnlyContain(c => PermissionCatalog.Contains(c));
            Perms(role).Should().NotContain(codes, $"'{role}' için geri alınan izin varsayılan pakette olmamalı");
        }
    }

    [Fact]
    public void Veteriner_Administrations_Correct_Is_Revoked_By_Seed()
    {
        RolePermissionBindings.Revoked["Veteriner"].Should().Contain(PermissionCatalog.Administrations.Correct);
    }
}
