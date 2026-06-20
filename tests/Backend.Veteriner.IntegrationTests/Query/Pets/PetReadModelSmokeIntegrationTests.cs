using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.Queries.GetList;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Pets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Query.Pets;

/// <summary>
/// Flag açık/kapalı smoke: <see cref="GetPetsListQueryHandler"/> doğru veri yolundan (Command vs Query DB)
/// okuyor ve tenant izolasyonu koruyor mu. Flag default <c>false</c>.
/// </summary>
[Collection("pet-projection")]
public sealed class PetReadModelSmokeIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public PetReadModelSmokeIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task FlagOff_Should_ServeFromCommandDb_EvenWhenReadModelEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenant = Guid.NewGuid();
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenant, "Command Path One");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenant, "Command Path Two");

        var result = await InvokeHandlerAsync(scope.ServiceProvider, tenant, petsEnabled: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(2);
        result.Value.Items.Should().OnlyContain(x => x.TenantId == tenant);
    }

    [Fact]
    public async Task FlagOn_Should_ServeFromReadModel_AfterProjection()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<Backend.Veteriner.Application.Projections.Pets.IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenant, "Read Model One");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenant, "Read Model Two");
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, otherTenant, "Other Tenant Row");

        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }

        var result = await InvokeHandlerAsync(scope.ServiceProvider, tenant, petsEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(2);
        result.Value.Items.Should().OnlyContain(x => x.TenantId == tenant);
    }

    [Fact]
    public async Task FlagOn_Should_ReturnEmpty_WhenReadModelNotPopulated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenant = Guid.NewGuid();
        await PetProjectionTestSupport.AddCommandPetWithCreatedEventAsync(commandDb, tenant, "No Fallback");

        var result = await InvokeHandlerAsync(scope.ServiceProvider, tenant, petsEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
    }

    private static async Task<Backend.Veteriner.Domain.Shared.Result<PagedResult<Backend.Veteriner.Application.Pets.Contracts.Dtos.PetListItemDto>>> InvokeHandlerAsync(
        IServiceProvider sp,
        Guid tenantId,
        bool petsEnabled)
    {
        var handler = new GetPetsListQueryHandler(
            new FixedTenantContext(tenantId),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IPetReadModelReader>(),
            Options.Create(new QueryReadModelsOptions { PetsEnabled = petsEnabled }));

        return await handler.Handle(new GetPetsListQuery(new PageRequest { Page = 1, PageSize = 50 }), CancellationToken.None);
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
