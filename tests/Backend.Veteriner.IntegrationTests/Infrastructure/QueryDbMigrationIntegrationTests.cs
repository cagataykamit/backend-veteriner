using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

[Collection("query-db-migration")]
public sealed class QueryDbMigrationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] ExpectedQueryTables =
    [
        "AppointmentReadModels",
        "ClientReadModels",
        "PetReadModels",
        "ClinicPetActivityReadModels",
        "ClinicClientActivityReadModels",
        "ClinicDailyAppointmentStatsReadModels",
        "ProcessedProjectionEvents"
    ];

    private static readonly string[] ForbiddenCommandTables =
    [
        "Appointments",
        "OutboxMessages",
        "Tenants",
        "Clinics",
        "Pets",
        "Clients"
    ];

    private readonly CustomWebApplicationFactory _factory;

    public QueryDbMigrationIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task QueryDbContext_Migration_Should_Create_ReadModelTables_Only()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            queryDb.Database.GetConnectionString(),
            allowedPrefix: IntegrationTestDatabaseGuard.IntegrationTestsQueryDatabaseName);

        await IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb);

        foreach (var table in ExpectedQueryTables)
        {
            (await TableExistsAsync(queryDb, table)).Should().BeTrue($"expected query table {table}");
        }

        foreach (var table in ForbiddenCommandTables)
        {
            (await TableExistsAsync(queryDb, table)).Should().BeFalse($"command table {table} must not exist in query DB");
        }

        var appointmentIndexes = await GetIndexNamesAsync(queryDb, "AppointmentReadModels");
        appointmentIndexes.Should().Contain("IX_AppointmentReadModels_TenantId_ClinicId_ScheduledAtUtc_AppointmentId");
        appointmentIndexes.Should().Contain("IX_AppointmentReadModels_TenantId_ClinicId_Status_ScheduledAtUtc");
        appointmentIndexes.Should().Contain("IX_AppointmentReadModels_TenantId_PetId");
        appointmentIndexes.Should().Contain("IX_AppointmentReadModels_TenantId_ClientId");

        var clientIndexes = await GetIndexNamesAsync(queryDb, "ClientReadModels");
        clientIndexes.Should().Contain("IX_ClientReadModels_TenantId_FullNameNormalized_ClientId");
        clientIndexes.Should().Contain("IX_ClientReadModels_TenantId_PhoneNormalized");
        clientIndexes.Should().Contain("IX_ClientReadModels_TenantId_Email");

        var petIndexes = await GetIndexNamesAsync(queryDb, "PetReadModels");
        petIndexes.Should().Contain("IX_PetReadModels_TenantId_NameNormalized_PetId");
        petIndexes.Should().Contain("IX_PetReadModels_TenantId_ClientId");
        petIndexes.Should().Contain("IX_PetReadModels_TenantId_ClientFullNameNormalized_PetId");
        petIndexes.Should().Contain("IX_PetReadModels_TenantId_SpeciesId");
        petIndexes.Should().Contain("IX_PetReadModels_TenantId_ColorId");
    }

    [Fact]
    public async Task ProcessedProjectionEvents_Should_Enforce_CompositePrimaryKey()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            queryDb.Database.GetConnectionString(),
            allowedPrefix: IntegrationTestDatabaseGuard.IntegrationTestsQueryDatabaseName);

        await IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb);

        var eventId = Guid.NewGuid();
        queryDb.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = eventId,
            ConsumerName = "appointment-projector",
            ProcessedAtUtc = DateTime.UtcNow
        });
        await queryDb.SaveChangesAsync();

        queryDb.ChangeTracker.Clear();
        queryDb.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = eventId,
            ConsumerName = "appointment-projector",
            ProcessedAtUtc = DateTime.UtcNow
        });

        var actDuplicate = () => queryDb.SaveChangesAsync();
        await actDuplicate.Should().ThrowAsync<DbUpdateException>();

        queryDb.ChangeTracker.Clear();
        queryDb.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = eventId,
            ConsumerName = "dashboard-projector",
            ProcessedAtUtc = DateTime.UtcNow
        });

        await queryDb.SaveChangesAsync();

        var count = await queryDb.ProcessedProjectionEvents
            .CountAsync(x => x.EventId == eventId);
        count.Should().Be(2);
    }

    [Fact]
    public async Task ClinicDailyAppointmentStats_Should_Persist_DateOnly_As_SqlDate()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            queryDb.Database.GetConnectionString(),
            allowedPrefix: IntegrationTestDatabaseGuard.IntegrationTestsQueryDatabaseName);

        await IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var localDate = new DateOnly(2026, 6, 16);

        queryDb.ClinicDailyAppointmentStatsReadModels.Add(new ClinicDailyAppointmentStatsReadModel
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            LocalDate = localDate,
            ScheduledCount = 3,
            CompletedCount = 1,
            CancelledCount = 1,
            TotalCount = 5,
            LastEventId = Guid.NewGuid(),
            LastProjectedAtUtc = DateTime.UtcNow
        });
        await queryDb.SaveChangesAsync();

        queryDb.ChangeTracker.Clear();
        var loaded = await queryDb.ClinicDailyAppointmentStatsReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.LocalDate == localDate);

        loaded.LocalDate.Should().Be(localDate);
        loaded.TotalCount.Should().Be(5);

        var columnType = await queryDb.Database
            .SqlQueryRaw<string>(
                """
                SELECT DATA_TYPE AS Value
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'ClinicDailyAppointmentStatsReadModels' AND COLUMN_NAME = 'LocalDate'
                """)
            .SingleAsync();

        columnType.Should().Be("date");
    }

    [Fact]
    public async Task PetReadModel_Should_Persist_NullableFields_And_BirthDate_As_SqlDate()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        IntegrationTestDatabaseGuard.EnsureSafeDatabase(
            queryDb.Database.GetConnectionString(),
            allowedPrefix: IntegrationTestDatabaseGuard.IntegrationTestsQueryDatabaseName);

        await IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb);

        var petId = Guid.NewGuid();
        var birthDate = new DateOnly(2024, 3, 10);

        queryDb.PetReadModels.Add(new PetReadModel
        {
            PetId = petId,
            TenantId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            ClientFullName = "Ada Lovelace",
            ClientFullNameNormalized = "ada lovelace",
            Name = "Rex",
            NameNormalized = "rex",
            SpeciesId = Guid.NewGuid(),
            SpeciesName = "Köpek",
            SpeciesNameNormalized = "kopek",
            BreedId = null,
            Breed = null,
            BreedRefName = null,
            ColorId = null,
            ColorName = null,
            ColorNameNormalized = null,
            Gender = 1,
            BirthDate = birthDate,
            Weight = 12.50m,
            LastEventId = Guid.NewGuid(),
            LastEventOccurredAtUtc = DateTime.UtcNow,
            LastProjectedAtUtc = DateTime.UtcNow
        });
        await queryDb.SaveChangesAsync();

        queryDb.ChangeTracker.Clear();
        var loaded = await queryDb.PetReadModels.SingleAsync(x => x.PetId == petId);
        loaded.BirthDate.Should().Be(birthDate);
        loaded.Weight.Should().Be(12.50m);
        loaded.BreedId.Should().BeNull();
        loaded.ColorId.Should().BeNull();

        var columnType = await queryDb.Database
            .SqlQueryRaw<string>(
                """
                SELECT DATA_TYPE AS Value
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'PetReadModels' AND COLUMN_NAME = 'BirthDate'
                """)
            .SingleAsync();

        columnType.Should().Be("date");
    }

    [Fact]
    public void CommandAndQueryIntegrationDatabaseNames_Should_MatchExpectedCatalogs()
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

    private static async Task<bool> TableExistsAsync(DbContext db, string tableName)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN OBJECT_ID(@table, 'U') IS NULL THEN 0 ELSE 1 END
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) == 1;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<IReadOnlyList<string>> GetIndexNamesAsync(DbContext db, string tableName)
    {
        return await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT i.name AS Value
                FROM sys.indexes i
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                WHERE t.name = {0} AND i.name IS NOT NULL
                """,
                tableName)
            .ToListAsync();
    }
}
