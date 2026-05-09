using Backend.Veteriner.Application.Auth;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>
/// Faz 4B-5 — Varsayılan rol yetkilerini ürün mantığına göre tamamlama.
/// <see cref="RolePermissionBindings.Map"/> içindeki Admin, Owner, ClinicAdmin, Veteriner, Sekreter
/// rolleri için ürün matrisini ve her rolden bilinçli olarak çıkarılan sistem/admin yetkilerini doğrular.
/// Tüm permission kodları <see cref="PermissionCatalog"/> ile birebir eşleşir; map içindeki kodlar
/// duplicate içermez.
/// </summary>
public sealed class RolePermissionBindingsMatrixTests
{
    private static IReadOnlyList<string> Perms(string roleName)
    {
        RolePermissionBindings.Map.TryGetValue(roleName, out var perms)
            .Should().BeTrue($"'{roleName}' rolü RolePermissionBindings.Map içinde tanımlı olmalı");
        return perms!;
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("ClinicAdmin")]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public void Map_Should_Contain_Role(string roleName)
    {
        RolePermissionBindings.Map.ContainsKey(roleName).Should().BeTrue();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("ClinicAdmin")]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public void Role_Permissions_Should_Be_Unique(string roleName)
    {
        var perms = Perms(roleName);
        perms.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            .Should().Be(perms.Count, $"'{roleName}' rolünde duplicate permission olmamalı");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("ClinicAdmin")]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public void Role_Permissions_Should_All_Exist_In_Catalog(string roleName)
    {
        var perms = Perms(roleName);
        var unknown = perms.Where(p => !PermissionCatalog.Contains(p)).ToList();
        unknown.Should().BeEmpty(
            $"'{roleName}' rolündeki tüm kodlar PermissionCatalog içinde tanımlı olmalı; bilinmeyen: {string.Join(", ", unknown)}");
    }

    // ---------- Pozitif beklentiler ----------

    public static IEnumerable<object[]> AdminExpectedPermissions => new[]
    {
        new object[] { PermissionCatalog.Dashboard.Read },
        new object[] { PermissionCatalog.Clients.Read },
        new object[] { PermissionCatalog.Clients.Create },
        new object[] { PermissionCatalog.Clients.Update },
        new object[] { PermissionCatalog.Pets.Read },
        new object[] { PermissionCatalog.Pets.Create },
        new object[] { PermissionCatalog.Pets.Update },
        new object[] { PermissionCatalog.Appointments.Read },
        new object[] { PermissionCatalog.Appointments.Create },
        new object[] { PermissionCatalog.Appointments.Cancel },
        new object[] { PermissionCatalog.Appointments.Complete },
        new object[] { PermissionCatalog.Appointments.Reschedule },
        new object[] { PermissionCatalog.Examinations.Read },
        new object[] { PermissionCatalog.Examinations.Create },
        new object[] { PermissionCatalog.Examinations.Update },
        new object[] { PermissionCatalog.Vaccinations.Read },
        new object[] { PermissionCatalog.Vaccinations.Create },
        new object[] { PermissionCatalog.Vaccinations.Update },
        new object[] { PermissionCatalog.Payments.Read },
        new object[] { PermissionCatalog.Payments.Create },
        new object[] { PermissionCatalog.Payments.Update },
        new object[] { PermissionCatalog.Treatments.Read },
        new object[] { PermissionCatalog.Treatments.Create },
        new object[] { PermissionCatalog.Treatments.Update },
        new object[] { PermissionCatalog.Prescriptions.Read },
        new object[] { PermissionCatalog.Prescriptions.Create },
        new object[] { PermissionCatalog.Prescriptions.Update },
        new object[] { PermissionCatalog.LabResults.Read },
        new object[] { PermissionCatalog.LabResults.Create },
        new object[] { PermissionCatalog.LabResults.Update },
        new object[] { PermissionCatalog.Hospitalizations.Read },
        new object[] { PermissionCatalog.Hospitalizations.Create },
        new object[] { PermissionCatalog.Hospitalizations.Update },
        new object[] { PermissionCatalog.Hospitalizations.Discharge },
        new object[] { PermissionCatalog.Clinics.Read },
        new object[] { PermissionCatalog.Clinics.Update },
        new object[] { PermissionCatalog.Reminders.Read },
        new object[] { PermissionCatalog.Reminders.Manage },
        new object[] { PermissionCatalog.Tenants.Read },
        new object[] { PermissionCatalog.Tenants.InviteCreate },
        new object[] { PermissionCatalog.Subscriptions.Read },

        new object[] { PermissionCatalog.ProductCategories.Read },
        new object[] { PermissionCatalog.ProductCategories.Create },
        new object[] { PermissionCatalog.ProductCategories.Update },
        new object[] { PermissionCatalog.ProductCategories.Deactivate },
        new object[] { PermissionCatalog.Products.Read },
        new object[] { PermissionCatalog.Products.Create },
        new object[] { PermissionCatalog.Products.Update },
        new object[] { PermissionCatalog.Products.Deactivate },
        new object[] { PermissionCatalog.StockMovements.Read },
        new object[] { PermissionCatalog.StockMovements.Create },
    };

    [Theory]
    [MemberData(nameof(AdminExpectedPermissions))]
    public void Admin_Should_Contain_Expected_Permission(string code)
    {
        Perms("Admin").Should().Contain(code);
    }

    [Theory]
    [MemberData(nameof(AdminExpectedPermissions))]
    public void Owner_Should_Contain_Same_Operational_Permissions_As_Admin(string code)
    {
        // Ürün kararı: Owner ≈ Admin (operasyon paketi aynı). Subscriptions.Manage burada verilmedi.
        Perms("Owner").Should().Contain(code);
    }

    public static IEnumerable<object[]> ClinicAdminExpectedPermissions => new[]
    {
        new object[] { PermissionCatalog.Dashboard.Read },
        new object[] { PermissionCatalog.Clients.Read },
        new object[] { PermissionCatalog.Clients.Create },
        new object[] { PermissionCatalog.Clients.Update },
        new object[] { PermissionCatalog.Pets.Read },
        new object[] { PermissionCatalog.Pets.Create },
        new object[] { PermissionCatalog.Pets.Update },
        new object[] { PermissionCatalog.Appointments.Read },
        new object[] { PermissionCatalog.Appointments.Create },
        new object[] { PermissionCatalog.Appointments.Cancel },
        new object[] { PermissionCatalog.Appointments.Complete },
        new object[] { PermissionCatalog.Appointments.Reschedule },
        new object[] { PermissionCatalog.Examinations.Read },
        new object[] { PermissionCatalog.Examinations.Create },
        new object[] { PermissionCatalog.Examinations.Update },
        new object[] { PermissionCatalog.Vaccinations.Read },
        new object[] { PermissionCatalog.Vaccinations.Create },
        new object[] { PermissionCatalog.Vaccinations.Update },
        new object[] { PermissionCatalog.Payments.Read },
        new object[] { PermissionCatalog.Payments.Create },
        new object[] { PermissionCatalog.Payments.Update },
        new object[] { PermissionCatalog.Treatments.Read },
        new object[] { PermissionCatalog.Treatments.Create },
        new object[] { PermissionCatalog.Treatments.Update },
        new object[] { PermissionCatalog.Prescriptions.Read },
        new object[] { PermissionCatalog.Prescriptions.Create },
        new object[] { PermissionCatalog.Prescriptions.Update },
        new object[] { PermissionCatalog.LabResults.Read },
        new object[] { PermissionCatalog.LabResults.Create },
        new object[] { PermissionCatalog.LabResults.Update },
        new object[] { PermissionCatalog.Hospitalizations.Read },
        new object[] { PermissionCatalog.Hospitalizations.Create },
        new object[] { PermissionCatalog.Hospitalizations.Update },
        new object[] { PermissionCatalog.Hospitalizations.Discharge },
        new object[] { PermissionCatalog.Clinics.Read },
        new object[] { PermissionCatalog.Clinics.Update },
        new object[] { PermissionCatalog.Reminders.Read },
        new object[] { PermissionCatalog.Reminders.Manage },
        new object[] { PermissionCatalog.Subscriptions.Read },

        new object[] { PermissionCatalog.ProductCategories.Read },
        new object[] { PermissionCatalog.ProductCategories.Create },
        new object[] { PermissionCatalog.ProductCategories.Update },
        new object[] { PermissionCatalog.ProductCategories.Deactivate },
        new object[] { PermissionCatalog.Products.Read },
        new object[] { PermissionCatalog.Products.Create },
        new object[] { PermissionCatalog.Products.Update },
        new object[] { PermissionCatalog.Products.Deactivate },
        new object[] { PermissionCatalog.StockMovements.Read },
        new object[] { PermissionCatalog.StockMovements.Create },
    };

    [Theory]
    [MemberData(nameof(ClinicAdminExpectedPermissions))]
    public void ClinicAdmin_Should_Contain_Expected_Permission(string code)
    {
        Perms("ClinicAdmin").Should().Contain(code);
    }

    public static IEnumerable<object[]> VeterinerExpectedPermissions => new[]
    {
        new object[] { PermissionCatalog.Dashboard.Read },
        new object[] { PermissionCatalog.Clients.Read },
        new object[] { PermissionCatalog.Pets.Read },
        new object[] { PermissionCatalog.Pets.Update },
        new object[] { PermissionCatalog.Appointments.Read },
        new object[] { PermissionCatalog.Appointments.Create },
        new object[] { PermissionCatalog.Appointments.Complete },
        new object[] { PermissionCatalog.Appointments.Reschedule },
        new object[] { PermissionCatalog.Examinations.Read },
        new object[] { PermissionCatalog.Examinations.Create },
        new object[] { PermissionCatalog.Examinations.Update },
        new object[] { PermissionCatalog.Vaccinations.Read },
        new object[] { PermissionCatalog.Vaccinations.Create },
        new object[] { PermissionCatalog.Vaccinations.Update },
        new object[] { PermissionCatalog.Treatments.Read },
        new object[] { PermissionCatalog.Treatments.Create },
        new object[] { PermissionCatalog.Treatments.Update },
        new object[] { PermissionCatalog.Prescriptions.Read },
        new object[] { PermissionCatalog.Prescriptions.Create },
        new object[] { PermissionCatalog.Prescriptions.Update },
        new object[] { PermissionCatalog.LabResults.Read },
        new object[] { PermissionCatalog.LabResults.Create },
        new object[] { PermissionCatalog.LabResults.Update },
        new object[] { PermissionCatalog.Hospitalizations.Read },
        new object[] { PermissionCatalog.Hospitalizations.Create },
        new object[] { PermissionCatalog.Hospitalizations.Update },
        new object[] { PermissionCatalog.Hospitalizations.Discharge },
        new object[] { PermissionCatalog.Payments.Read },
        new object[] { PermissionCatalog.ProductCategories.Read },
        new object[] { PermissionCatalog.Products.Read },
        new object[] { PermissionCatalog.StockMovements.Read },
        new object[] { PermissionCatalog.Reminders.Read },
    };

    [Theory]
    [MemberData(nameof(VeterinerExpectedPermissions))]
    public void Veteriner_Should_Contain_Expected_Permission(string code)
    {
        Perms("Veteriner").Should().Contain(code);
    }

    public static IEnumerable<object[]> SekreterExpectedPermissions => new[]
    {
        new object[] { PermissionCatalog.Dashboard.Read },
        new object[] { PermissionCatalog.Clients.Read },
        new object[] { PermissionCatalog.Clients.Create },
        new object[] { PermissionCatalog.Clients.Update },
        new object[] { PermissionCatalog.Pets.Read },
        new object[] { PermissionCatalog.Pets.Create },
        new object[] { PermissionCatalog.Pets.Update },
        new object[] { PermissionCatalog.Appointments.Read },
        new object[] { PermissionCatalog.Appointments.Create },
        new object[] { PermissionCatalog.Appointments.Cancel },
        new object[] { PermissionCatalog.Appointments.Reschedule },
        new object[] { PermissionCatalog.Examinations.Read },
        new object[] { PermissionCatalog.Vaccinations.Read },
        new object[] { PermissionCatalog.Treatments.Read },
        new object[] { PermissionCatalog.Prescriptions.Read },
        new object[] { PermissionCatalog.LabResults.Read },
        new object[] { PermissionCatalog.Hospitalizations.Read },
        new object[] { PermissionCatalog.Payments.Read },
        new object[] { PermissionCatalog.Payments.Create },
        new object[] { PermissionCatalog.Payments.Update },
        new object[] { PermissionCatalog.ProductCategories.Read },
        new object[] { PermissionCatalog.Products.Read },
        new object[] { PermissionCatalog.StockMovements.Read },
        new object[] { PermissionCatalog.StockMovements.Create },
        new object[] { PermissionCatalog.Reminders.Read },
    };

    [Theory]
    [MemberData(nameof(SekreterExpectedPermissions))]
    public void Sekreter_Should_Contain_Expected_Permission(string code)
    {
        Perms("Sekreter").Should().Contain(code);
    }

    // ---------- Negatif (sistem/admin yetkileri klinik rollerine verilmemeli) ----------

    public static IEnumerable<object[]> SystemPermissionsForbiddenForClinicRoles()
    {
        var roles = new[] { "ClinicAdmin", "Veteriner", "Sekreter" };
        var systemCodes = new[]
        {
            PermissionCatalog.Admin.Diagnostics,
            PermissionCatalog.Outbox.Read,
            PermissionCatalog.Outbox.Write,
            PermissionCatalog.Roles.Read,
            PermissionCatalog.Roles.Write,
            PermissionCatalog.Permissions.Read,
            PermissionCatalog.Permissions.Write,
            PermissionCatalog.Users.Read,
            PermissionCatalog.Users.Write,
            PermissionCatalog.Tenants.Create,
            PermissionCatalog.Tenants.InviteCreate,
            PermissionCatalog.Subscriptions.Manage,
            PermissionCatalog.Clinics.Create,
        };

        foreach (var role in roles)
            foreach (var code in systemCodes)
                yield return new object[] { role, code };
    }

    [Theory]
    [MemberData(nameof(SystemPermissionsForbiddenForClinicRoles))]
    public void Clinic_Roles_Should_Not_Contain_System_Permission(string roleName, string code)
    {
        Perms(roleName).Should().NotContain(code,
            $"'{roleName}' klinik rolü '{code}' sistem/admin yetkisini almamalı");
    }

    [Fact]
    public void Admin_Should_Not_Contain_System_Permissions()
    {
        // Admin tenant rolü; sistem yetkileri (Outbox, Diagnostics, Permissions/Roles/Users yazma) verilmez.
        // Platform Admin claim'i ayrı bir mekanizma (AdminClaimSeeder) ile bunlara zaten erişir.
        var admin = Perms("Admin");
        admin.Should().NotContain(PermissionCatalog.Admin.Diagnostics);
        admin.Should().NotContain(PermissionCatalog.Outbox.Read);
        admin.Should().NotContain(PermissionCatalog.Outbox.Write);
        admin.Should().NotContain(PermissionCatalog.Roles.Write);
        admin.Should().NotContain(PermissionCatalog.Permissions.Write);
        admin.Should().NotContain(PermissionCatalog.Users.Write);
        admin.Should().NotContain(PermissionCatalog.Tenants.Create);
        admin.Should().NotContain(PermissionCatalog.Subscriptions.Manage);
    }

    [Fact]
    public void Owner_Should_Not_Contain_System_Permissions()
    {
        var owner = Perms("Owner");
        owner.Should().NotContain(PermissionCatalog.Admin.Diagnostics);
        owner.Should().NotContain(PermissionCatalog.Outbox.Read);
        owner.Should().NotContain(PermissionCatalog.Outbox.Write);
        owner.Should().NotContain(PermissionCatalog.Roles.Write);
        owner.Should().NotContain(PermissionCatalog.Permissions.Write);
        owner.Should().NotContain(PermissionCatalog.Users.Write);
        owner.Should().NotContain(PermissionCatalog.Tenants.Create);
    }

    // ---------- Rol-spesifik kısıtlamalar (ürün matrisi) ----------

    [Fact]
    public void Veteriner_Should_Not_Contain_Client_Or_Payment_Write_Permissions()
    {
        var vet = Perms("Veteriner");
        vet.Should().NotContain(PermissionCatalog.Clients.Create, "Veteriner müşteri oluşturmaz (operasyon işi)");
        vet.Should().NotContain(PermissionCatalog.Clients.Update, "Veteriner müşteri güncellemez (operasyon işi)");
        vet.Should().NotContain(PermissionCatalog.Payments.Create, "Veteriner ödeme oluşturmaz");
        vet.Should().NotContain(PermissionCatalog.Payments.Update, "Veteriner ödeme güncellemez");
        vet.Should().NotContain(PermissionCatalog.Appointments.Cancel, "Randevu iptali resepsiyon/clinic admin işi");
        vet.Should().NotContain(PermissionCatalog.Clinics.Update, "Veteriner klinik profil güncellemez");
    }

    [Fact]
    public void Sekreter_Should_Not_Contain_Medical_Write_Permissions()
    {
        var sec = Perms("Sekreter");
        sec.Should().NotContain(PermissionCatalog.Appointments.Complete, "Randevu tamamlama veteriner işi");
        sec.Should().NotContain(PermissionCatalog.Examinations.Create);
        sec.Should().NotContain(PermissionCatalog.Examinations.Update);
        sec.Should().NotContain(PermissionCatalog.Vaccinations.Create);
        sec.Should().NotContain(PermissionCatalog.Vaccinations.Update);
        sec.Should().NotContain(PermissionCatalog.Treatments.Create);
        sec.Should().NotContain(PermissionCatalog.Treatments.Update);
        sec.Should().NotContain(PermissionCatalog.Prescriptions.Create);
        sec.Should().NotContain(PermissionCatalog.Prescriptions.Update);
        sec.Should().NotContain(PermissionCatalog.LabResults.Create);
        sec.Should().NotContain(PermissionCatalog.LabResults.Update);
        sec.Should().NotContain(PermissionCatalog.Hospitalizations.Create);
        sec.Should().NotContain(PermissionCatalog.Hospitalizations.Update);
        sec.Should().NotContain(PermissionCatalog.Hospitalizations.Discharge);
        sec.Should().NotContain(PermissionCatalog.Reminders.Manage, "Reminder yönetimi clinic admin/owner işi");
    }

    [Fact]
    public void ClinicAdmin_Should_Not_Contain_Tenant_Or_Clinic_Create_Or_Subscriptions_Manage()
    {
        var ca = Perms("ClinicAdmin");
        ca.Should().NotContain(PermissionCatalog.Clinics.Create, "Yeni klinik açma tenant Admin/Owner işi");
        ca.Should().NotContain(PermissionCatalog.Tenants.InviteCreate, "Üye davet tenant Admin/Owner işi");
        ca.Should().NotContain(PermissionCatalog.Tenants.Read);
        ca.Should().NotContain(PermissionCatalog.Tenants.Create);
        ca.Should().NotContain(PermissionCatalog.Subscriptions.Manage);
    }

    [Fact]
    public void Map_Should_Contain_Exactly_Five_Default_Roles()
    {
        // Faz 4B-5 sonrası varsayılan rol seti; yeni rol eklenirse bu test bilinçli olarak güncellenmelidir.
        // Faz 4B-6: PlatformAdmin bilinçli olarak Map'in dışındadır — platform/sistem yetkileri
        // tenant-level role tablosunda yer almamalı.
        RolePermissionBindings.Map.Keys.Should().BeEquivalentTo(
            new[] { "Admin", "Owner", "ClinicAdmin", "Veteriner", "Sekreter" },
            options => options.WithoutStrictOrdering());
    }

    // ---------- Faz 4B-6: PlatformAdmin ayrımı ----------

    [Fact]
    public void Map_Should_Not_Contain_PlatformAdmin()
    {
        // PlatformAdmin tenant-level rol değildir; tüm permission'ları AdminClaimSeeder üzerinden
        // doğrudan claim'e bağlanır. RolePermissionBindings tenant rol tablosunda yer almamalıdır.
        RolePermissionBindings.Map.ContainsKey("PlatformAdmin")
            .Should().BeFalse(
                "PlatformAdmin tenant role binding map'inde yer almamalı; AdminClaimSeeder üzerinden tüm permission'lara bağlanır");
    }

    [Fact]
    public void InviteAssignableOperationClaimsCatalog_Should_Not_Contain_PlatformAdmin()
    {
        // PlatformAdmin tenant davet whitelist'inde olmamalı; tenant kullanıcıları davet ederken seçilemez.
        Backend.Veteriner.Application.Tenants.InviteAssignableOperationClaimsCatalog
            .NamesInDisplayOrder
            .Should().NotContain("PlatformAdmin",
                "PlatformAdmin platform-level rol; tenant davet/atama akışlarında görünmemeli");

        Backend.Veteriner.Application.Tenants.InviteAssignableOperationClaimsCatalog
            .IsAssignableName("PlatformAdmin")
            .Should().BeFalse();
    }
}
