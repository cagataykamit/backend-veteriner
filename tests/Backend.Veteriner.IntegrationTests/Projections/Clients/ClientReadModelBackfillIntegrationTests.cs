using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Clients;

[Collection("client-projection")]
public sealed class ClientReadModelBackfillIntegrationTests
{
    private readonly ClientProjectionWebApplicationFactory _factory;

    public ClientReadModelBackfillIntegrationTests(ClientProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Backfill_WithEmptyQueryDb_Should_FillReadModels_AndBeInSync()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();

        await SeedCommandClientsAsync(tenantId, "Ayşe Yılmaz", "Mehmet Demir", "Cem Çelik");

        var result = await RunBackfillAsync(tenantId);

        result.Success.Should().BeTrue();
        result.ScopeTenantId.Should().Be(tenantId);
        result.CommandClientCount.Should().Be(3);
        result.QueryClientCount.Should().Be(3);
        result.InsertedCount.Should().Be(3);
        result.UpdatedCount.Should().Be(0);
        result.SkippedStaleCount.Should().Be(0);
        result.ParityInSync.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var rows = await queryDb.ClientReadModels.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();
        rows.Should().HaveCount(3);
        rows.Select(x => x.ClientId).Should().OnlyHaveUniqueItems();
        rows.Should().AllSatisfy(x =>
        {
            x.LastEventId.Should().Be(ClientReadModelBackfillService.BackfillEventId);
            x.LastEventOccurredAtUtc.Should().Be(x.CreatedAtUtc, "backfill snapshot ordering key = UpdatedAtUtc ?? CreatedAtUtc");
            x.LastProjectedAtUtc.Should().BeOnOrAfter(x.CreatedAtUtc);
        });
    }

    [Fact]
    public async Task Backfill_RunTwice_Should_BeIdempotent()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();
        await SeedCommandClientsAsync(tenantId, "Idem One", "Idem Two");

        var first = await RunBackfillAsync(tenantId);
        var second = await RunBackfillAsync(tenantId);

        first.InsertedCount.Should().Be(2);
        second.InsertedCount.Should().Be(0);
        second.UpdatedCount.Should().Be(2, "aynı occurredAt'te re-run güvenli update yapar, duplicate üretmez");
        second.QueryClientCount.Should().Be(2);
        second.ParityInSync.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.ClientReadModels.CountAsync(x => x.TenantId == tenantId)).Should().Be(2);
    }

    [Fact]
    public async Task Backfill_Should_UpdateExistingRow_WhenCommandRowChanged()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();
        var clientId = await SeedSingleCommandClientAsync(tenantId, "Önceki İsim", email: "onceki@example.com");

        await RunBackfillAsync(tenantId);

        await using (var mutateScope = _factory.Services.CreateAsyncScope())
        {
            var commandDb = mutateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = await commandDb.Clients.SingleAsync(c => c.Id == clientId);
            client.UpdateDetails("Yeni İsim", "yeni@example.com", client.Phone, client.Address);
            await commandDb.SaveChangesAsync();
        }

        var second = await RunBackfillAsync(tenantId);
        second.UpdatedCount.Should().Be(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var row = await queryDb.ClientReadModels.AsNoTracking().SingleAsync(x => x.ClientId == clientId);
        row.FullName.Should().Be("Yeni İsim");
        row.Email.Should().Be("yeni@example.com");
        (await queryDb.ClientReadModels.CountAsync(x => x.ClientId == clientId)).Should().Be(1);
    }

    [Fact]
    public async Task Backfill_TenantScoped_Should_IsolateTenants()
    {
        await ResetQuerySideAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedCommandClientsAsync(tenantA, "A One", "A Two");
        await SeedCommandClientsAsync(tenantB, "B One", "B Two", "B Three");

        var result = await RunBackfillAsync(tenantA);

        result.ScopeTenantId.Should().Be(tenantA);
        result.CommandClientCount.Should().Be(2);
        result.InsertedCount.Should().Be(2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.ClientReadModels.CountAsync(x => x.TenantId == tenantA)).Should().Be(2);
        (await queryDb.ClientReadModels.CountAsync(x => x.TenantId == tenantB)).Should().Be(0, "tenant scope diğer tenant'ı yazmamalı");
    }

    [Fact]
    public async Task Backfill_Should_NotWriteProcessedProjectionEvents()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();
        await SeedCommandClientsAsync(tenantId, "No Fake Event");

        await RunBackfillAsync(tenantId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.ProcessedProjectionEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Backfill_Should_NotOverwriteRow_WrittenByNewerEvent()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();
        var clientId = await SeedSingleCommandClientAsync(tenantId, "Command Side", email: "command@example.com");

        // Query'de bu client için daha YENİ bir event ile yazılmış satır var (gerçek projection).
        var newerOccurredAt = DateTime.UtcNow.AddDays(1);
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = seedScope.ServiceProvider.GetRequiredService<QueryDbContext>();
            queryDb.ClientReadModels.Add(new ClientReadModel
            {
                ClientId = clientId,
                TenantId = tenantId,
                FullName = "Newer Event Name",
                FullNameNormalized = "newer event name",
                Email = "newer@example.com",
                Phone = null,
                PhoneNormalized = null,
                CreatedAtUtc = DateTime.UtcNow,
                LastEventId = Guid.NewGuid(),
                LastProjectedAtUtc = DateTime.UtcNow,
                LastEventOccurredAtUtc = newerOccurredAt
            });
            await queryDb.SaveChangesAsync();
        }

        var result = await RunBackfillAsync(tenantId);
        result.SkippedStaleCount.Should().Be(1);
        result.UpdatedCount.Should().Be(0);

        await using var scope = _factory.Services.CreateAsyncScope();
        var verifyDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var row = await verifyDb.ClientReadModels.AsNoTracking().SingleAsync(x => x.ClientId == clientId);
        row.FullName.Should().Be("Newer Event Name", "daha yeni event ile yazılmış satır backfill tarafından ezilmemeli");
        row.LastEventOccurredAtUtc.Should().Be(newerOccurredAt);
    }

    [Fact]
    public async Task Backfill_AllTenants_Should_ProduceGlobalParityInSync()
    {
        await ResetQuerySideAsync();
        await SeedCommandClientsAsync(Guid.NewGuid(), "Global One", "Global Two");

        var result = await RunBackfillAsync(tenantId: null);

        result.ScopeTenantId.Should().BeNull();
        result.ParityInSync.Should().BeTrue();
        result.CommandClientCount.Should().Be(result.QueryClientCount);

        await using var scope = _factory.Services.CreateAsyncScope();
        var parity = scope.ServiceProvider.GetRequiredService<IClientReadModelParityReader>();
        (await parity.GetGlobalParityAsync()).InSync.Should().BeTrue();
    }

    private async Task<ClientReadModelBackfillResult> RunBackfillAsync(Guid? tenantId, int batchSize = 500)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var backfill = scope.ServiceProvider.GetRequiredService<IClientReadModelBackfillService>();
        return await backfill.BackfillAsync(tenantId, batchSize, CancellationToken.None);
    }

    private async Task ResetQuerySideAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
    }

    private async Task SeedCommandClientsAsync(Guid tenantId, params string[] fullNames)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var name in fullNames)
            commandDb.Clients.Add(new Client(tenantId, name));
        await commandDb.SaveChangesAsync();
    }

    private async Task<Guid> SeedSingleCommandClientAsync(Guid tenantId, string fullName, string? email = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = new Client(tenantId, fullName, phone: null, email: email);
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();
        return client.Id;
    }
}
