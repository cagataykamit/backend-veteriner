using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Persistence;

/// <summary>Komut veritabanı migration ile READ_COMMITTED_SNAPSHOT açık kurulur (canlıyla aynı okuma davranışı).</summary>
public sealed class ReadCommittedSnapshotTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ReadCommittedSnapshotTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Command_Database_Should_Have_Read_Committed_Snapshot_On()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var enabled = await db.Database
            .SqlQueryRaw<bool>("SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE database_id = DB_ID()")
            .SingleAsync();

        enabled.Should().BeTrue();
    }
}
