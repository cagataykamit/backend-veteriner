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
    public void CommandIntegrationTestsDatabaseName_Should_BeAllowed()
    {
        var commandDatabaseName = IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            IntegrationTestDatabaseGuard.DedicatedConnectionString);

        commandDatabaseName.Should().Be("VetinityCommandDb_IntegrationTests");
    }

    [Fact]
    public void CommandIntegrationTestsDatabaseRunPrefix_Should_BeAllowed()
    {
        var commandDatabaseName = IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            Conn("VetinityCommandDb_IntegrationTests_Run42"));

        commandDatabaseName.Should().Be("VetinityCommandDb_IntegrationTests_Run42");
    }

    [Theory]
    [InlineData("VeterinerDb")]
    [InlineData("VeterinerDb_IntegrationTests")]
    [InlineData("VetinityDb")]
    [InlineData("VetinityDb_IntegrationTests")]
    [InlineData("VetinityLoadTestDb")]
    [InlineData("VetinityQueryLoadTestDb")]
    public void LegacyDatabaseNames_Should_BeRejected(string legacyDatabaseName)
    {
        var act = () => IntegrationTestDatabaseGuard.EnsureSafeDatabase(Conn(legacyDatabaseName));

        act.Should().Throw<IntegrationTestDatabaseSafetyException>();
    }

    [Theory]
    [InlineData("VetinityCommandDb")]
    [InlineData("VetinityQueryDb")]
    [InlineData("VetinityCommandDb_LoadTest")]
    [InlineData("VetinityQueryDb_LoadTest")]
    [InlineData("VeterinerDb_Development")]
    [InlineData("VeterinerDb_Production")]
    [InlineData("VeterinerProd")]
    [InlineData("SomeOtherDb")]
    public void NonIntegrationCommandDatabaseNames_Should_BeRejected(string databaseName)
    {
        var act = () => IntegrationTestDatabaseGuard.EnsureSafeDatabase(Conn(databaseName));

        act.Should().Throw<IntegrationTestDatabaseSafetyException>();
    }

    [Fact]
    public void LegacyVeterinerDbName_Should_BeRejected()
    {
        // Doğrulama senaryosu #2: bilerek legacy VeterinerDb verildiğinde startup güvenlik exception'ı.
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
    public void EnsureSafeDatabase_Should_Throw_For_EmptyCommandDatabaseName()
    {
        var act = () => IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            "Server=(localdb)\\mssqllocaldb;Trusted_Connection=True");

        act.Should().Throw<IntegrationTestDatabaseSafetyException>();
    }
}

/// <summary>
/// Doğrulama senaryosu #1: host'un efektif command veritabanı adı VetinityCommandDb_IntegrationTests olmalı.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class IntegrationTestEffectiveCommandDatabaseTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public IntegrationTestEffectiveCommandDatabaseTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void EffectiveCommandDatabaseName_Should_Be_VetinityCommandDbIntegrationTests()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connectionString = db.Database.GetConnectionString();
        connectionString.Should().NotBeNullOrWhiteSpace();

        var commandDatabaseName = new SqlConnectionStringBuilder(connectionString!).InitialCatalog;
        commandDatabaseName.Should().Be("VetinityCommandDb_IntegrationTests");
    }
}
