using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureServices(services =>
        {
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            // Database schema migration baseline apply
            db.Database.Migrate();
            TestDataSeeder.Seed(db, hasher);

            // Production boot ile aynı permission zinciri: catalog → DB Permissions, Admin claim → tüm izinler, admin kullanıcı → Admin claim.
            // TestDataSeeder yalnızca IntegrationTasksAdmin + Outbox bağlar; policy tabanlı modüller (Prescriptions, Treatments, …) için AdminClaimSeeder şart.
            PermissionSeeder.SeedAsync(db).GetAwaiter().GetResult();
            AdminClaimSeeder.SeedAsync(db).GetAwaiter().GetResult();
            InviteAssignableOperationClaimsSeeder.SeedAsync(db).GetAwaiter().GetResult();
        });
    }
}
