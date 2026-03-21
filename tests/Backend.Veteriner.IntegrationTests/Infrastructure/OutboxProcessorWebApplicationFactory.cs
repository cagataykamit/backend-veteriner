using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Paylaşılan Backend_Veteriner_IntegrationTests veritabanında OutboxProcessor çakışmasını önlemek için
/// ayrı LocalDB veritabanı ve hızlı döngü/no-op SMTP kullanır.
/// </summary>
public sealed class OutboxProcessorWebApplicationFactory : WebApplicationFactory<global::Program>
{
    private const string ProcessorTestConnection =
        "Server=(localdb)\\mssqllocaldb;Database=Backend_Veteriner_OutboxProcessorTests;Trusted_Connection=True;MultipleActiveResultSets=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ProcessorTestConnection
            });
        });

        builder.ConfigureServices(services =>
        {
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            db.Database.EnsureCreated();
            TestDataSeeder.Seed(db, hasher);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSenderImmediate>();
            services.AddScoped<IEmailSenderImmediate, NoOpEmailSenderImmediate>();
            services.PostConfigure<OutboxOptions>(o =>
            {
                o.LoopIntervalSeconds = 2;
                o.BatchSize = 50;
            });
        });
    }
}
