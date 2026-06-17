using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// DB güvenlik guard'ının saf (DB bağlantısı açmayan) birim doğrulamaları.
/// </summary>
public sealed class IntegrationTestDatabaseGuardTests
{
    private static string Conn(string database)
        => $"Server=DESKTOP-2U2UUHO;Database={database};Trusted_Connection=True;TrustServerCertificate=True";

    [Fact]
    public void EnsureSafeDatabase_Should_Allow_DedicatedIntegrationTestsDatabase()
    {
        var db = IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            IntegrationTestDatabaseGuard.DedicatedConnectionString);

        db.Should().Be("VetinityCommandDb_IntegrationTests");
    }

    [Fact]
    public void EnsureSafeDatabase_Should_Allow_RunSpecificPrefixedName()
    {
        var db = IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            Conn("VetinityCommandDb_IntegrationTests_Run42"));

        db.Should().Be("VetinityCommandDb_IntegrationTests_Run42");
    }

    [Theory]
    [InlineData("VeterinerDb")]
    [InlineData("VeterinerDb_IntegrationTests")]
    [InlineData("VetinityDb")]
    [InlineData("VetinityDb_IntegrationTests")]
    [InlineData("VetinityCommandDb")]
    [InlineData("VetinityQueryDb")]
    [InlineData("VetinityLoadTestDb")]
    [InlineData("VetinityCommandDb_LoadTest")]
    [InlineData("VeterinerDb_Development")]
    [InlineData("VeterinerDb_Production")]
    [InlineData("VeterinerProd")]
    [InlineData("SomeOtherDb")]
    public void EnsureSafeDatabase_Should_Throw_For_ForbiddenOrNonAllowlistedNames(string database)
    {
        var act = () => IntegrationTestDatabaseGuard.EnsureSafeDatabase(Conn(database));

        act.Should().Throw<IntegrationTestDatabaseSafetyException>();
    }

    [Fact]
    public void EnsureSafeDatabase_Should_Throw_For_VeterinerDb_Deliberately()
    {
        // Doğrulama senaryosu #2: bilerek VeterinerDb verildiğinde startup güvenlik exception'ı.
        var act = () => IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            "Server=DESKTOP-2U2UUHO;Database=VeterinerDb;Trusted_Connection=True;TrustServerCertificate=True");

        act.Should().Throw<IntegrationTestDatabaseSafetyException>()
            .WithMessage("*VeterinerDb*");
    }

    [Fact]
    public void EnsureSafeDatabase_Should_Throw_For_EmptyConnectionString()
    {
        var act = () => IntegrationTestDatabaseGuard.EnsureSafeDatabase("");

        act.Should().Throw<IntegrationTestDatabaseSafetyException>();
    }

    [Fact]
    public void EnsureSafeDatabase_Should_Throw_For_EmptyDatabaseName()
    {
        var act = () => IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            "Server=(localdb)\\mssqllocaldb;Trusted_Connection=True");

        act.Should().Throw<IntegrationTestDatabaseSafetyException>();
    }
}

/// <summary>
/// Doğrulama senaryosu #1: host'un efektif veritabanı adı VetinityCommandDb_IntegrationTests olmalı.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class IntegrationTestEffectiveDatabaseTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public IntegrationTestEffectiveDatabaseTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void EffectiveDatabase_Should_Be_IntegrationTestsDatabase()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connectionString = db.Database.GetConnectionString();
        connectionString.Should().NotBeNullOrWhiteSpace();

        var databaseName = new SqlConnectionStringBuilder(connectionString!).InitialCatalog;
        databaseName.Should().Be("VetinityCommandDb_IntegrationTests");
    }
}
