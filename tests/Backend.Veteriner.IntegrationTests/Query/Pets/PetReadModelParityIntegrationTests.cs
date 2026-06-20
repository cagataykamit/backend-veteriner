using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Pets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Pets;

[Collection("pet-projection")]
public sealed class PetReadModelParityIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public PetReadModelParityIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task TenantParity_Should_BeInSync_AfterAllPetEventsProjected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();
        var parity = scope.ServiceProvider.GetRequiredService<IPetReadModelParityReader>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenantA, "Pamuk");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenantA, "Boncuk");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenantA, "Karabaş");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenantB, "Other Tenant Pet");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenantB, "Second Other Pet");

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
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();
        var parity = scope.ServiceProvider.GetRequiredService<IPetReadModelParityReader>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var emptyTenant = Guid.NewGuid();

        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenantA, "Tenant A Pet");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenantB, "Tenant B Pet One");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenantB, "Tenant B Pet Two");

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
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var parity = scope.ServiceProvider.GetRequiredService<IPetReadModelParityReader>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenant = Guid.NewGuid();
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenant, "Unprojected One");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenant, "Unprojected Two");

        var result = await parity.GetTenantParityAsync(tenant);

        result.CommandCount.Should().Be(2);
        result.QueryCount.Should().Be(0);
        result.Difference.Should().Be(2);
        result.InSync.Should().BeFalse();
    }

    private static async Task DrainAsync(IPetProjectionProcessor processor)
    {
        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }
    }
}
