using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.DemoRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// Uygulamanın ana EF Core DbContext'i.
/// - Domain entity'lerinin SQL Server karşılıklarını yönetir.
/// - Fluent API tanımları ve external configuration'ları birleştirir.
/// - Outbox, auth, authorization ve user persistence işlemlerinin merkezidir.
/// </summary>
public class AppDbContext : TenantIsolatedDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options, TenantContext tenantContext)
        : base(options, tenantContext) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Model ile migration snapshot arasında uyumsuzluk olduğunda (ör. geliştirme sırasında)
        // PendingModelChangesWarning'ın exception'a dönüşmesini engellemek için kullanılır.
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        base.OnConfiguring(optionsBuilder);
    }

    // =========================================================
    // DbSets
    // Kurumsal standart:
    // - DbSet'ler "=> Set<T>()" ile expose edilir.
    // - Null olma riski yoktur.
    // - EF Core runtime tarafından yönetilir.
    // =========================================================
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<VerificationToken> VerificationTokens => Set<VerificationToken>();

    public DbSet<OperationClaim> OperationClaims => Set<OperationClaim>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserOperationClaim> UserOperationClaims => Set<UserOperationClaim>();
    public DbSet<OperationClaimPermission> OperationClaimPermissions => Set<OperationClaimPermission>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantActivation> TenantActivations => Set<TenantActivation>();
    public DbSet<DemoRequest> DemoRequests => Set<DemoRequest>();
    public DbSet<ScheduledSubscriptionPlanChange> ScheduledSubscriptionPlanChanges => Set<ScheduledSubscriptionPlanChange>();
    public DbSet<BillingCheckoutSession> BillingCheckoutSessions => Set<BillingCheckoutSession>();
    public DbSet<BillingWebhookReceipt> BillingWebhookReceipts => Set<BillingWebhookReceipt>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<TenantInvite> TenantInvites => Set<TenantInvite>();
    public DbSet<LegalAcceptance> LegalAcceptances => Set<LegalAcceptance>();

    // Hasta sahibi portalı (ADR-031)
    public DbSet<OwnerAccount> OwnerAccounts => Set<OwnerAccount>();
    public DbSet<OwnerAccountClientLink> OwnerAccountClientLinks => Set<OwnerAccountClientLink>();
    public DbSet<OwnerPortalInvite> OwnerPortalInvites => Set<OwnerPortalInvite>();
    public DbSet<OwnerPortalCode> OwnerPortalCodes => Set<OwnerPortalCode>();
    public DbSet<OwnerPortalRefreshToken> OwnerPortalRefreshTokens => Set<OwnerPortalRefreshToken>();

    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<UserClinic> UserClinics => Set<UserClinic>();
    public DbSet<ClinicWorkingHour> ClinicWorkingHours => Set<ClinicWorkingHour>();
    public DbSet<ClinicAppointmentSettings> ClinicAppointmentSettings => Set<ClinicAppointmentSettings>();
    public DbSet<ClinicKvkkFormTemplate> ClinicKvkkFormTemplates => Set<ClinicKvkkFormTemplate>();
    public DbSet<ClinicTaxpayerSettings> ClinicTaxpayerSettings => Set<ClinicTaxpayerSettings>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Pet> Pets => Set<Pet>();
    public DbSet<Species> Species => Set<Species>();
    public DbSet<Breed> Breeds => Set<Breed>();
    public DbSet<PetColor> PetColors => Set<PetColor>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Examination> Examinations => Set<Examination>();
    public DbSet<ExaminationAddendum> ExaminationAddenda => Set<ExaminationAddendum>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<Vaccination> Vaccinations => Set<Vaccination>();
    public DbSet<VaccineDefinition> VaccineDefinitions => Set<VaccineDefinition>();
    public DbSet<ClinicalTerm> ClinicalTerms => Set<ClinicalTerm>();
    public DbSet<ExaminationTemplate> ExaminationTemplates => Set<ExaminationTemplate>();
    public DbSet<ExaminationTemplateLine> ExaminationTemplateLines => Set<ExaminationTemplateLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Administration> Administrations => Set<Administration>();
    public DbSet<ChargeSuggestion> ChargeSuggestions => Set<ChargeSuggestion>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Treatment> Treatments => Set<Treatment>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<ImagingRecord> ImagingRecords => Set<ImagingRecord>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<Hospitalization> Hospitalizations => Set<Hospitalization>();
    public DbSet<HospitalizationWard> HospitalizationWards => Set<HospitalizationWard>();
    public DbSet<HospitalizationRoom> HospitalizationRooms => Set<HospitalizationRoom>();
    public DbSet<CareInstruction> CareInstructions => Set<CareInstruction>();
    public DbSet<TenantBillingProfile> TenantBillingProfiles => Set<TenantBillingProfile>();
    public DbSet<TenantReminderSettings> TenantReminderSettings => Set<TenantReminderSettings>();
    public DbSet<ReminderDispatchLog> ReminderDispatchLogs => Set<ReminderDispatchLog>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchRow> ImportBatchRows => Set<ImportBatchRow>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockBatch> StockBatches => Set<StockBatch>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // =========================================================
        // USERS
        // - User ana tablosu
        // - Owned collection: UserRoles
        // - Email unique constraint
        // =========================================================
        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);

            e.Property(x => x.Email)
                .IsRequired()
                .HasMaxLength(200);

            e.Property(x => x.DisplayName)
                .HasMaxLength(User.MaxDisplayNameLength);

            e.Property(x => x.PasswordHash)
                .IsRequired();

            e.Property(x => x.EmailConfirmed)
                .IsRequired();

            e.Property(x => x.CreatedAtUtc)
                .IsRequired();

            e.Property(x => x.UpdatedAtUtc);

            e.Property(x => x.FailedLoginCount)
                .IsRequired();

            e.Property(x => x.LockedUntilUtc);

            e.HasIndex(x => x.Email)
                .IsUnique();

            // User.Roles -> Owned Entity (UserRoles tablosu)
            e.OwnsMany(x => x.Roles, r =>
            {
                r.ToTable("UserRoles");
                r.WithOwner().HasForeignKey("UserId");

                // Owned collection için surrogate key
                r.Property<int>("Id").ValueGeneratedOnAdd();
                r.HasKey("Id");

                r.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                // Aynı kullanıcıya aynı rol adı bir kez atanabilsin
                r.HasIndex("UserId", "Name")
                    .IsUnique();
            });

            // RefreshTokens navigation ayrı tablo/config üzerinden yönetiliyor.
            // Mapping'ler ApplyConfigurationsFromAssembly ile alınır.
        });

        // =========================================================
        // OUTBOX
        // - Transactional Outbox pattern için mesaj tablosu
        // - Retry / dead-letter / correlation alanları burada tutulur
        // =========================================================
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("Outbox");

            e.HasKey(x => x.Id);
            e.Property(x => x.Id)
                .ValueGeneratedNever();

            e.Property(x => x.Type)
                .IsRequired()
                .HasMaxLength(300); // domain event full name için 100 yetersiz kalabilir

            e.Property(x => x.Payload)
                .IsRequired();

            e.Property(x => x.CreatedAtUtc)
                .IsRequired();

            e.Property(x => x.ProcessedAtUtc);
            e.Property(x => x.NextAttemptAtUtc);
            e.Property(x => x.DeadLetterAtUtc);

            e.Property(x => x.RetryCount)
                .IsRequired();

            // Hata ve teşhis alanları
            e.Property(x => x.LastError)
                .HasMaxLength(1000);

            e.Property(x => x.Error)
                .HasMaxLength(4000);

            e.Property(x => x.CorrelationId)
                .HasMaxLength(100);

            e.Property(x => x.TraceId)
                .HasMaxLength(100);

            // En sık sorgulanan alanlar için index'ler
            e.HasIndex(x => new { x.NextAttemptAtUtc, x.ProcessedAtUtc });
            e.HasIndex(x => x.DeadLetterAtUtc);
            e.HasIndex(x => x.CreatedAtUtc);
        });

        // =========================================================
        // EXTERNAL CONFIGURATIONS
        // - IEntityTypeConfiguration<T> implementasyonları otomatik taranır
        //   Örn:
        //   RefreshTokenConfiguration
        //   PermissionConfiguration
        //   OperationClaimConfiguration
        //   UserOperationClaimConfiguration
        //   OperationClaimPermissionConfiguration
        // =========================================================
        b.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly,
            type => type.Namespace is null || !type.Namespace.Contains(".Persistence.Query.", StringComparison.Ordinal));

        ApplyTenantFilters(b);

        base.OnModelCreating(b);
    }
}
