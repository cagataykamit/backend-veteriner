using System.Reflection;
using System.Text.RegularExpressions;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Tenancy;

/// <summary>
/// Kiracı izolasyonu mimari kuralları (ADR-040): kiracılı varlık işaretsiz kalamaz ve kiracı filtresini kapatan sistem
/// kapsamı yalnız gerekçesi bilinen yerlerde kullanılır. Yeni kullanım bu listeye bilinçli olarak eklenir.
/// </summary>
public sealed class TenantIsolationArchitectureTests
{
    /// <summary>
    /// Sistem kapsamı (<c>AsSystem</c>, <c>CreateSystemScope</c>, <c>[PlatformTenantScope]</c>) kullanılan kaynak dosyalar
    /// (src'ye göre) ve gerekçesi.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedSystemScopeFiles = new Dictionary<string, string>
    {
        // Yardımcılar (çağıranları da bu listededir)
        ["Backend.Veteriner.Infrastructure/Persistence/TenantServiceScopes.cs"] = "Arka plan kapsam yardımcısı",
        ["Backend.Veteriner.Api/Auth/PlatformTenantScopeAttribute.cs"] = "Platform yönetimi eylem filtresi",
        // Kimlik ve anonim uçlar: kiracı JWT'den değil kayıttan çözülür
        ["Backend.Veteriner.Infrastructure/Persistence/Repositories/UserTenantRepository.cs"] = "Kullanıcının üyelikleri (giriş, tek kiracı kuralı)",
        ["Backend.Veteriner.Application/OwnerPortal/Access/OwnerPortalAccess.cs"] = "Portal bağı hesaba göre aranır",
        ["Backend.Veteriner.Application/OwnerPortal/Invites/OwnerPortalInviteResolver.cs"] = "Portal daveti token ile aranır",
        ["Backend.Veteriner.Application/Public/Commands/AcceptInvite/AcceptTenantInviteCommandHandler.cs"] = "Personel daveti token ile aranır",
        ["Backend.Veteriner.Application/Public/Commands/SignupAndAcceptInvite/SignupAndAcceptTenantInviteCommandHandler.cs"] = "Personel daveti token ile aranır",
        ["Backend.Veteriner.Application/Public/Queries/InviteDetail/GetPublicTenantInviteDetailQueryHandler.cs"] = "Personel daveti token ile aranır",
        ["Backend.Veteriner.Application/Files/Queries/GetSignedContent/GetSignedStoredFileContentQuery.cs"] = "İmzalı dosya bağlantısı",
        ["Backend.Veteriner.Application/Tenants/Billing/SubscriptionCheckoutActivationService.cs"] = "Webhook: oturum kimlikle aranır",
        ["Backend.Veteriner.Application/Tenants/Commands/Billing/ProcessBillingWebhookCommandHandler.cs"] = "Webhook: oturum kimlikle aranır",
        ["Backend.Veteriner.Application/Tenants/Commands/Billing/ConfirmCheckoutReturnCommandHandler.cs"] = "Sağlayıcı dönüşü: oturum kimlik ya da token ile aranır",
        ["Backend.Veteriner.Application/SalesPartners/DiscountCodeRedemptionService.cs"] = "Kod kullanım limiti tüm kiracılarda sayılır",
        // Platform yönetimi
        ["Backend.Veteriner.Api/Controllers/SalesPartnersAdminController.cs"] = "Satış ortakları (platform yöneticisi)",
        ["Backend.Veteriner.Infrastructure/Tenants/TrialActivationMetricsReader.cs"] = "Deneme metrikleri (platform yöneticisi)",
        // Arka plan ve bakım
        ["Backend.Veteriner.Infrastructure/Imports/ImportValidationProcessor.cs"] = "Kuyruk taraması",
        ["Backend.Veteriner.Infrastructure/Imports/ImportCommitProcessor.cs"] = "Kuyruk taraması",
        ["Backend.Veteriner.Infrastructure/Imports/ImportRollbackProcessor.cs"] = "Kuyruk taraması",
        ["Backend.Veteriner.Infrastructure/Imports/ImportMaintenanceProcessor.cs"] = "Bakım taraması",
        ["Backend.Veteriner.Infrastructure/Files/StoredFileMaintenanceProcessor.cs"] = "Bakım taraması",
        ["Backend.Veteriner.Infrastructure/Billing/ScheduledPlanChangeProcessorHostedService.cs"] = "Vadesi gelen plan değişiklikleri",
        ["Backend.Veteriner.Infrastructure/Billing/CheckoutReconciliationProcessor.cs"] = "Ödeme oturumlarının sağlayıcıyla uzlaştırması",
        ["Backend.Veteriner.Infrastructure/Reminders/ReminderProcessorService.cs"] = "Hatırlatması açık kiracılar ve günlük uzlaştırması",
        ["Backend.Veteriner.Infrastructure/Projections/Appointments/AppointmentProjectionRebuildService.cs"] = "Okuma modelini yeniden kurma",
        ["Backend.Veteriner.Infrastructure/Projections/Clients/ClientReadModelBackfillService.cs"] = "Okuma modeli doldurma (kiracısız çağrı)",
        ["Backend.Veteriner.Infrastructure/Projections/Payments/PaymentFinanceBackfillService.cs"] = "Okuma modeli doldurma (kiracısız çağrı)",
        ["Backend.Veteriner.Infrastructure/Projections/Payments/PaymentReadModelBackfillService.cs"] = "Okuma modeli doldurma (kiracısız çağrı)",
        ["Backend.Veteriner.Infrastructure/Projections/Payments/PaymentReadModelHealthReader.cs"] = "Sağlık sinyali sayımı",
        ["Backend.Veteriner.Infrastructure/Projections/Pets/PetReadModelBackfillService.cs"] = "Okuma modeli doldurma (kiracısız çağrı)",
        ["Backend.Veteriner.Infrastructure/Projections/Appointments/AppointmentProjectionHostedService.cs"] = "Projeksiyon işçisi",
        ["Backend.Veteriner.Infrastructure/Projections/Clients/ClientProjectionHostedService.cs"] = "Projeksiyon işçisi",
        ["Backend.Veteriner.Infrastructure/Projections/Payments/PaymentProjectionHostedService.cs"] = "Projeksiyon işçisi",
        ["Backend.Veteriner.Infrastructure/Projections/Pets/PetProjectionHostedService.cs"] = "Projeksiyon işçisi",
        ["Backend.Veteriner.DbMigrator/Program.cs"] = "Migration, seed, bakım komutları",
    };

    private static readonly Regex SystemScopeUsage = new(@"\.AsSystem\(\)|CreateSystemScope\(|\[PlatformTenantScope\]", RegexOptions.Compiled);

    [Fact]
    public void Domain_Entities_With_TenantId_Should_Implement_ITenantOwned()
    {
        var unmarked = typeof(ITenantOwned).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.GetProperty(nameof(ITenantOwned.TenantId), BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(Guid)
                        && !typeof(ITenantOwned).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        unmarked.Should().BeEmpty("TenantId taşıyan varlık kiracı filtresine girmek için ITenantOwned uygulamalıdır (ADR-040)");
    }

    [Fact]
    public void Tenantless_Domain_Entities_Should_Not_Carry_TenantId()
    {
        string[] tenantless = ["Tenant", "User", "Species", "Breed", "PetColor", "DiscountCode"];
        var carrying = typeof(ITenantOwned).Assembly.GetTypes()
            .Where(t => tenantless.Contains(t.Name) && t.GetProperty(nameof(ITenantOwned.TenantId)) is not null)
            .Select(t => t.FullName)
            .ToList();

        carrying.Should().BeEmpty();
    }

    [Fact]
    public void System_Scope_Should_Only_Be_Used_In_Allowed_Files()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var used = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => SystemScopeUsage.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(src, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet();

        used.Except(AllowedSystemScopeFiles.Keys).Should().BeEmpty(
            "kiracı filtresini kapatan sistem kapsamı gerekçesiyle izinli listeye eklenmelidir (ADR-040)");
        AllowedSystemScopeFiles.Keys.Except(used).Should().BeEmpty("kullanımı kalkan dosya listeden çıkarılmalıdır");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Veteriner.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Veteriner.sln bulunamadı.");
    }
}
