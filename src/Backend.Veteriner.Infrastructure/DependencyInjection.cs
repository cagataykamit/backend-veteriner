// src/Backend.Veteriner.Infrastructure/DependencyInjection.cs
using Backend.Veteriner.Application.Auth.Contracts;                              // IPermissionReader
using Backend.Veteriner.Application.Common.Abstractions;                        // IPasswordHasher, IJwtTokenService, ITokenHashService, IClientContext, IUser..., IRefreshTokenRepository, IVerificationTokenRepository, IEmailSender, IEmailSenderImmediate, IAppUrlProvider
using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Outbox;                               // IOutboxBuffer
using Backend.Veteriner.Application.Common.Auditing;                            // IAuditLogWriter, IAuditContext
using Backend.Veteriner.Infrastructure.Caching;                                  // PermissionReader
using Backend.Veteriner.Infrastructure.Mailing;                                   // MailKitEmailSender, TransactionalEmailSender, SmtpOptions
using Backend.Veteriner.Infrastructure.Outbox;                                    // EfOutbox, OutboxProcessor, OutboxOptions, OutboxBuffer, OutboxSaveChangesInterceptor
using Backend.Veteriner.Infrastructure.Persistence;                               // AppDbContext
using Backend.Veteriner.Infrastructure.Persistence.Repositories;                 // EfRepository, EfReadRepository, UserRepository, RefreshTokenRepository, VerificationTokenRepository
using Backend.Veteriner.Infrastructure.Persistence.Repositories.OperationClaimPermissions;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.OperationClaims;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.Permissions;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.UserOperationClaims;
using Backend.Veteriner.Infrastructure.Security;                                  // JwtOptions, JwtTokenService, Sha256TokenHashService, BcryptPasswordHasher, JwtOptionsProvider
using Backend.Veteriner.Infrastructure.Web;                                       // ClientContext, TenantContext, ClinicContext, AppUrlProvider, HttpAuditContext
using Backend.Veteriner.Infrastructure.Auditing;                                  // AuditLogWriter
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

        // ===== Outbox =====
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));

        services.AddScoped<OutboxBuffer>();
        services.AddScoped<IOutboxBuffer>(sp => sp.GetRequiredService<OutboxBuffer>());
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<DomainEventOutboxInterceptor>();
        services.AddSingleton<DomainEventTypeRegistry>();

        // ===== DbContext (TEK KAYIT) =====
        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseSqlServer(connStr);
            opt.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });

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

        // ===== Outbox background worker =====
        services.AddHostedService<OutboxProcessor>();

        // ===== RefreshToken cleanup background worker =====
        services.Configure<RefreshTokenCleanupOptions>(configuration.GetSection("RefreshTokenCleanup"));
        services.AddHostedService<RefreshTokenCleanupHostedService>();

        // ===== Session options =====
        services.Configure<SessionOptions>(configuration.GetSection("Session"));

        // ===== Permission options =====
        services.Configure<PermissionChangeOptions>(configuration.GetSection("PermissionChange"));

        /// ===== Unit of Work =====
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // TransactionBehavior, MediatR pipeline'ında çalışır; her request için transaction başlatır ve commit/rollback yapar.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));


        return services;
    }
}
