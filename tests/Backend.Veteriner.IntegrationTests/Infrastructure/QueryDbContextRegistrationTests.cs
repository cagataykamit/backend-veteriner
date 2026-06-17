using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// CQRS-1: QueryDbContext DI kaydı ve command/query DB ayrımı smoke testleri.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class QueryDbContextRegistrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public QueryDbContextRegistrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void Should_Resolve_AppDbContext_And_QueryDbContext()
    {
        using var scope = _factory.Services.CreateScope();

        var act = () =>
        {
            _ = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            _ = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void CommandAndQueryIntegrationDatabaseNames_Should_UseDifferentCatalogs()
    {
        using var scope = _factory.Services.CreateScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        var commandDatabaseName = new SqlConnectionStringBuilder(appDb.Database.GetConnectionString()!).InitialCatalog;
        var queryDatabaseName = new SqlConnectionStringBuilder(queryDb.Database.GetConnectionString()!).InitialCatalog;

        commandDatabaseName.Should().Be(IntegrationTestDatabaseGuard.IntegrationTestsCommandDatabaseName);
        queryDatabaseName.Should().Be(IntegrationTestDatabaseGuard.IntegrationTestsQueryDatabaseName);
        commandDatabaseName.Should().NotBe(queryDatabaseName);
    }

    [Fact]
    public void QueryDbContext_Should_Not_Register_Interceptors()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<QueryDbContext>>();

        var extension = options.Extensions.OfType<CoreOptionsExtension>().SingleOrDefault();
        var interceptors = extension?.Interceptors;

        interceptors.Should().BeNullOrEmpty();
    }
}
