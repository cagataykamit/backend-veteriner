using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Clients;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Clients;

[Collection("client-projection")]
public sealed class ClientReadModelParityIntegrationTests
{
    private readonly ClientProjectionWebApplicationFactory _factory;

    public ClientReadModelParityIntegrationTests(ClientProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task TenantParity_Should_BeInSync_AfterAllClientEventsProjected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();
        var parity = scope.ServiceProvider.GetRequiredService<IClientReadModelParityReader>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenantA, "Ayşe Yılmaz");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenantA, "Mehmet Demir");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenantA, "Cem Çelik");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenantB, "Other Tenant");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenantB, "Second Other");

        await DrainAsync(processor);

        var parityA = await parity.GetTenantParityAsync(tenantA);
        parityA.CommandCount.Should().Be(3);
        parityA.QueryCount.Should().Be(3);
        parityA.InSync.Should().BeTrue();
        parityA.ScopeTenantId.Should().Be(tenantA);

        var parityB = await parity.GetTenantParityAsync(tenantB);
        parityB.CommandCount.Should().Be(2);
        parityB.QueryCount.Should().Be(2);
        parityB.InSync.Should().BeTrue();
    }

    [Fact]
    public async Task TenantParity_Should_IsolateTenants()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();
        var parity = scope.ServiceProvider.GetRequiredService<IClientReadModelParityReader>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var emptyTenant = Guid.NewGuid();

        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenantA, "Tenant A One");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenantB, "Tenant B One");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenantB, "Tenant B Two");

        await DrainAsync(processor);

        (await parity.GetTenantParityAsync(tenantA)).CommandCount.Should().Be(1);
        (await parity.GetTenantParityAsync(tenantB)).QueryCount.Should().Be(2);

        var emptyParity = await parity.GetTenantParityAsync(emptyTenant);
        emptyParity.CommandCount.Should().Be(0);
        emptyParity.QueryCount.Should().Be(0);
        emptyParity.InSync.Should().BeTrue();
    }

    [Fact]
    public async Task TenantParity_Should_ReportReadModelBehind_WhenEventsNotProjected()
    {
        // Backfill caveat: command satırları var ama read-model boş → parity in-sync DEĞİL.
        // (Backfill/rebuild CQRS-12B-6 konusudur; bu fazda projeksiyon yapılmaz.)
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var parity = scope.ServiceProvider.GetRequiredService<IClientReadModelParityReader>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenant = Guid.NewGuid();
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenant, "Unprojected One");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenant, "Unprojected Two");

        // Bilinçli olarak processor çalıştırılmaz.
        var result = await parity.GetTenantParityAsync(tenant);

        result.CommandCount.Should().Be(2);
        result.QueryCount.Should().Be(0);
        result.Difference.Should().Be(2);
        result.InSync.Should().BeFalse();
    }

    private static async Task DrainAsync(IClientProjectionProcessor processor)
    {
        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }
    }
}
