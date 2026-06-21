// src/Backend.Veteriner.Infrastructure/DependencyInjection.cs
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Auth.Contracts;                              // IPermissionReader
using Backend.Veteriner.Application.Common.Abstractions;                        // IPasswordHasher, IJwtTokenService, ITokenHashService, IClientContext, IUser..., IRefreshTokenRepository, IVerificationTokenRepository, IEmailSender, IEmailSenderImmediate, IAppUrlProvider
using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Outbox;                               // IOutboxBuffer
using Backend.Veteriner.Application.Common.Auditing;                            // IAuditLogWriter, IAuditContext
using Backend.Veteriner.Infrastructure.Caching;                                  // PermissionReader
using Backend.Veteriner.Infrastructure.Mailing;                                   // MailKitEmailSender, TransactionalEmailSender, SmtpOptions
using Backend.Veteriner.Infrastructure.Outbox;                                    // EfOutbox, OutboxProcessor, OutboxOptions, OutboxBuffer, OutboxSaveChangesInterceptor
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Backend.Veteriner.Infrastructure.Query.Appointments;
using Backend.Veteriner.Infrastructure.Query.Clients;
using Backend.Veteriner.Infrastructure.Query.Pets;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.Veteriner.Infrastructure.Query.Payments;
using Backend.Veteriner.Infrastructure.Persistence.Query;
using Backend.Veteriner.Infrastructure.Query.Dashboard;
using Backend.Veteriner.Infrastructure.Persistence;                               // AppDbContext
using Backend.Veteriner.Infrastructure.Persistence.Repositories;                 // EfRepository, EfReadRepository, UserRepository, RefreshTokenRepository, VerificationTokenRepository
using Backend.Veteriner.Infrastructure.Persistence.Repositories.Dashboard;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.OperationClaimPermissions;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.OperationClaims;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.Permissions;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.UserOperationClaims;
using Backend.Veteriner.Infrastructure.Reminders;
using Backend.Veteriner.Infrastructure.Security;                                  // JwtOptions, JwtTokenService, Sha256TokenHashService, BcryptPasswordHasher, JwtOptionsProvider
using Backend.Veteriner.Infrastructure.Web;                                       // ClientContext, TenantContext, ClinicContext, AppUrlProvider, HttpAuditContext, CurrentUserPermissionChecker
using Backend.Veteriner.Infrastructure.Auditing;                                  // AuditLogWriter
using Backend.Veteriner.Infrastructure.Billing;
using Backend.Veteriner.Application.Common.Billing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ===== Connection string =====
        // Kurumsal standart: DefaultConnection
        var connStr = configuration.GetConnectionString("DefaultConnection");

        // Geriye dönük uyumluluk: eski key varsa onu da kabul et
        if (string.IsNullOrWhiteSpace(connStr))
            connStr = configuration.GetConnectionString("SqlServer")
                     ?? configuration["ConnectionStrings:SqlServer"];

        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException(
                "Connection string bulunamadı. 'ConnectionStrings:DefaultConnection' (önerilen) veya 'ConnectionStrings:SqlServer' tanımlayın.");

        var queryConnStr = configuration.GetConnectionString("QueryConnection");
        if (string.IsNullOrWhiteSpace(queryConnStr))
            throw new InvalidOperationException(
                "Connection string bulunamadı. 'ConnectionStrings:QueryConnection' tanımlayın.");

        // ===== Outbox =====
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        services.Configure<AppointmentProjectionOptions>(
            configuration.GetSection(AppointmentProjectionOptions.SectionName));
        services.AddSingleton<IValidateOptions<AppointmentProjectionOptions>, AppointmentProjectionOptionsValidator>();
        services.AddOptions<AppointmentProjectionOptions>().ValidateOnStart();
        services.Configure<ClientProjectionOptions>(
            configuration.GetSection(ClientProjectionOptions.SectionName));
        services.Configure<PetProjectionOptions>(
            configuration.GetSection(PetProjectionOptions.SectionName));
        services.Configure<PaymentProjectionOptions>(
            configuration.GetSection(PaymentProjectionOptions.SectionName));
        services.Configure<PaymentProjectionHealthOptions>(
            configuration.GetSection(PaymentProjectionHealthOptions.SectionName));
        services.Configure<ClientProjectionHealthOptions>(
            configuration.GetSection(ClientProjectionHealthOptions.SectionName));
        services.Configure<PetProjectionHealthOptions>(
            configuration.GetSection(PetProjectionHealthOptions.SectionName));
        services.Configure<QueryReadModelsOptions>(
            configuration.GetSection(QueryReadModelsOptions.SectionName));
        services.Configure<AppointmentProjectionHealthOptions>(
            configuration.GetSection(AppointmentProjectionHealthOptions.SectionName));
        services.Configure<AppointmentProjectionMonitoringOptions>(
            configuration.GetSection(AppointmentProjectionMonitoringOptions.SectionName));
        services.AddSingleton<IValidateOptions<AppointmentProjectionMonitoringOptions>, AppointmentProjectionMonitoringOptionsValidator>();
        services.AddOptions<AppointmentProjectionMonitoringOptions>().ValidateOnStart();
        services.Configure<PerformanceDiagnosticsOptions>(
            configuration.GetSection(PerformanceDiagnosticsOptions.SectionName));

        services.AddSingleton<AppointmentProjectionMetricsSnapshotHolder>();
        services.AddSingleton<AppointmentProjectionMetrics>();

        services.AddSingleton(TimeProvider.System);

        services.AddScoped<OutboxBuffer>();
        services.AddScoped<IOutboxBuffer>(sp => sp.GetRequiredService<OutboxBuffer>());
        services.AddScoped<IAppointmentIntegrationEventOutbox, AppointmentIntegrationEventOutbox>();
        services.AddScoped<IClientIntegrationEventOutbox, ClientIntegrationEventOutbox>();
        services.AddScoped<IPetIntegrationEventOutbox, PetIntegrationEventOutbox>();
        services.AddScoped<IPaymentIntegrationEventOutbox, PaymentIntegrationEventOutbox>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<SlowQueryLoggingInterceptor>();
        services.AddScoped<DbConnectionSlowOpenInterceptor>();
        services.AddScoped<DomainEventOutboxInterceptor>();
        services.AddSingleton<DomainEventTypeRegistry>();

        // ===== DbContext (command) =====
        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseSqlServer(connStr);
            opt.AddInterceptors(
                sp.GetRequiredService<OutboxSaveChangesInterceptor>(),
                sp.GetRequiredService<SlowQueryLoggingInterceptor>(),
                sp.GetRequiredService<DbConnectionSlowOpenInterceptor>());
        });

        // ===== DbContext (query — projection read-model; interceptor yok) =====
        services.AddDbContext<QueryDbContext>(opt => opt.UseSqlServer(queryConnStr));

        // ===== Repositories =====
        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserReadRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IVerificationTokenRepository, VerificationTokenRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IOperationClaimPermissionRepository, OperationClaimPermissionRepository>();
        services.AddScoped<IOperationClaimReadRepository, OperationClaimReadRepository>();
        services.AddScoped<IUserOperationClaimRepository, UserOperationClaimRepository>();
        services.AddScoped<IUserTenantRepository, UserTenantRepository>();
        services.AddScoped<IUserClinicRepository, UserClinicRepository>();
        services.AddScoped<IDashboardTodayAppointmentStatusCountsReader, DashboardTodayAppointmentStatusCountsReader>();
        services.AddScoped<IDashboardClinicScopedReader, DashboardClinicScopedReader>();
        services.AddScoped<IDashboardFinancePaymentAggregatesReader, DashboardFinancePaymentAggregatesReader>();
        services.AddScoped<IAppointmentsReportStatusBreakdownReader, AppointmentsReportStatusBreakdownReader>();
        services.AddScoped<IOutbox, EfOutbox>();



        // ===== Security =====
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<ITokenHashService, Sha256TokenHashService>();


        // ===== JWT =====
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IJwtOptionsProvider, JwtOptionsProvider>();

        // ===== HttpContext & App URL & Audit =====
        services.AddHttpContextAccessor();
        services.AddScoped<IClientContext, ClientContext>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IClinicContext, ClinicContext>();
        services.AddScoped<ICurrentUserPermissionChecker, CurrentUserPermissionChecker>();
        services.AddScoped<ICurrentUserRoleAccessor, CurrentUserRoleAccessor>();
        services.AddScoped<IAppUrlProvider, AppUrlProvider>();
        services.AddScoped<IAuditContext, HttpAuditContext>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();

        // ===== Mail =====
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();
        services.AddScoped<IEmailSenderImmediate, MailKitEmailSender>(); // SMTP ile gönderir
        services.AddScoped<IEmailSender, TransactionalEmailSender>();    // Outbox'a yazar

        // ===== Permissions cache/reader =====
        services.AddMemoryCache();
        services.AddScoped<IPermissionReader, PermissionReader>();

        // Permission cache invalidation (rol/permission değişince cache düşürmek için)
        services.AddScoped<IPermissionCacheInvalidator, PermissionCacheInvalidator>();

        // Global species/breed katalog liste cache
        services.AddSingleton<CatalogListCache>();
        services.AddSingleton<ICatalogListCache>(sp => sp.GetRequiredService<CatalogListCache>());
        services.AddSingleton<ICatalogCacheInvalidator>(sp => sp.GetRequiredService<CatalogListCache>());

        // ===== Outbox background worker =====
        services.AddHostedService<OutboxProcessor>();

        services.AddScoped<IAppointmentProjectionSnapshotFactory, AppointmentProjectionSnapshotFactory>();
        services.AddScoped<IAppointmentProjectionProcessor, AppointmentProjectionProcessor>();
        services.AddScoped<IAppointmentOutboxClaimRepository, SqlAppointmentOutboxClaimRepository>();
        services.AddSingleton<IAppointmentProjectionWorkerIdentity, AppointmentProjectionWorkerIdentity>();
        services.AddScoped<IAppointmentProjectionRebuildService, AppointmentProjectionRebuildService>();
        services.AddScoped<IAppointmentProjectionStatusReader, AppointmentProjectionStatusReader>();
        services.AddScoped<IQueryDatabaseStatusReader, QueryDatabaseStatusReader>();
        services.AddScoped<IAppointmentReadModelReader, AppointmentReadModelReader>();
        services.AddScoped<IDashboardAppointmentReadModelReader, DashboardAppointmentReadModelReader>();
        services.AddScoped<IDashboardFinanceReadModelReader, DashboardFinanceReadModelReader>();
        services.AddHostedService<AppointmentProjectionHostedService>();

        // ===== Client projection (CQRS-12B-3) =====
        services.AddScoped<IClientProjectionProcessor, ClientProjectionProcessor>();
        services.AddScoped<IClientOutboxClaimRepository, SqlClientOutboxClaimRepository>();
        services.AddSingleton<IClientProjectionWorkerIdentity, ClientProjectionWorkerIdentity>();
        services.AddHostedService<ClientProjectionHostedService>();

        // ===== Client query read-model reader (CQRS-12B-4) =====
        services.AddScoped<IClientReadModelReader, ClientReadModelReader>();
        services.AddScoped<IClientReadModelLookupReader, ClientReadModelReader>();

        // ===== Pet query read-model reader (CQRS-12C-4) =====
        services.AddScoped<IPetReadModelReader, PetReadModelReader>();
        services.AddScoped<IPetReadModelLookupReader, PetReadModelReader>();

        // ===== Client projection health / parity (CQRS-12B-5) =====
        services.AddScoped<IClientProjectionStatusReader, ClientProjectionStatusReader>();
        services.AddScoped<IClientReadModelParityReader, ClientReadModelParityReader>();

        // ===== Pet projection health / parity (CQRS-12C-5) =====
        services.AddScoped<IPetProjectionStatusReader, PetProjectionStatusReader>();
        services.AddScoped<IPetReadModelParityReader, PetReadModelParityReader>();

        // ===== Client read-model backfill / rebuild (CQRS-12B-6) =====
        services.AddScoped<IClientReadModelBackfillService, ClientReadModelBackfillService>();

        // ===== Pet read-model backfill / rebuild (CQRS-12C-6) =====
        services.AddScoped<IPetReadModelBackfillService, PetReadModelBackfillService>();

        // ===== Pet projection (CQRS-12C-3) =====
        services.AddScoped<IPetProjectionProcessor, PetProjectionProcessor>();
        services.AddScoped<IPetOutboxClaimRepository, SqlPetOutboxClaimRepository>();
        services.AddSingleton<IPetProjectionWorkerIdentity, PetProjectionWorkerIdentity>();
        services.AddHostedService<PetProjectionHostedService>();

        // ===== Payment finance projection (CQRS-13C) =====
        services.AddScoped<IPaymentProjectionProcessor, PaymentProjectionProcessor>();
        services.AddScoped<IPaymentOutboxClaimRepository, SqlPaymentOutboxClaimRepository>();
        services.AddSingleton<IPaymentProjectionWorkerIdentity, PaymentProjectionWorkerIdentity>();
        services.AddHostedService<PaymentProjectionHostedService>();

        // ===== Payment finance projection health / parity / backfill (CQRS-13D) =====
        services.AddScoped<IPaymentProjectionStatusReader, PaymentProjectionStatusReader>();
        services.AddScoped<IPaymentFinanceParityReader, PaymentFinanceParityReader>();
        services.AddScoped<IPaymentFinanceBackfillService, PaymentFinanceBackfillService>();

        // ===== RefreshToken cleanup background worker =====
        services.Configure<RefreshTokenCleanupOptions>(configuration.GetSection("RefreshTokenCleanup"));
        services.AddHostedService<RefreshTokenCleanupHostedService>();

        // ===== Session options =====
        services.Configure<SessionOptions>(configuration.GetSection("Session"));

        services.Configure<BillingOptions>(configuration.GetSection("Billing"));
        services.Configure<ScheduledPlanChangeProcessorOptions>(configuration.GetSection("Billing:ScheduledPlanChangeProcessor"));
        services.AddScoped<IBillingCheckoutProvider, ManualBillingCheckoutProvider>();
        services.AddScoped<IBillingCheckoutProvider, StripeBillingCheckoutProvider>();
        services.AddScoped<IBillingCheckoutProvider, IyzicoBillingCheckoutProvider>();
        services.AddScoped<IBillingCheckoutProviderResolver, BillingCheckoutProviderResolver>();
        services.AddScoped<IBillingWebhookSignatureVerifier, BillingWebhookSignatureVerifier>();
        services.AddScoped<IBillingWebhookPayloadParser, BillingWebhookPayloadParser>();
        services.AddHostedService<ScheduledPlanChangeProcessorHostedService>();

        services.Configure<ReminderProcessorOptions>(configuration.GetSection("Reminders:Processor"));
        services.AddScoped<IReminderEmailOutboxEnqueuer, ReminderEmailOutboxEnqueuer>();
        services.AddScoped<ReminderProcessorService>();
        services.AddHostedService<ReminderProcessorHostedService>();

        // ===== Permission options =====
        services.Configure<PermissionChangeOptions>(configuration.GetSection("PermissionChange"));

        /// ===== Unit of Work =====
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // TransactionBehavior, MediatR pipeline'ında çalışır; her request için transaction başlatır ve commit/rollback yapar.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));


        return services;
    }
}
