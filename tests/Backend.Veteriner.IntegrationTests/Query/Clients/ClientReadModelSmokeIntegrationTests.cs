using Backend.Veteriner.Application.Clients.Queries.GetList;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Clients;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Query.Clients;

/// <summary>
/// Flag açık/kapalı smoke: <see cref="GetClientsListQueryHandler"/> doğru veri yolundan (Command vs Query DB)
/// okuyor ve tenant izolasyonu koruyor mu. Flag default <c>false</c>.
/// </summary>
[Collection("client-projection")]
public sealed class ClientReadModelSmokeIntegrationTests
{
    private readonly ClientProjectionWebApplicationFactory _factory;

    public ClientReadModelSmokeIntegrationTests(ClientProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task FlagOff_Should_ServeFromCommandDb_EvenWhenReadModelEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenant = Guid.NewGuid();
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenant, "Command Path One");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenant, "Command Path Two");
        // Read-model bilinçli olarak boş bırakılır (projeksiyon çalıştırılmaz).

        var result = await InvokeHandlerAsync(scope.ServiceProvider, tenant, clientsEnabled: false);

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
        var processor = scope.ServiceProvider.GetRequiredService<Backend.Veteriner.Application.Projections.Clients.IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenant, "Read Model One");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenant, "Read Model Two");
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, otherTenant, "Other Tenant Row");

        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }

        var result = await InvokeHandlerAsync(scope.ServiceProvider, tenant, clientsEnabled: true);

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

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenant = Guid.NewGuid();
        // Command tarafında satır var ama projeksiyon yapılmadı → flag açıkken Command DB'ye fallback YOK.
        await ClientProjectionTestSupport.AddCommandClientWithCreatedEventAsync(commandDb, tenant, "No Fallback");

        var result = await InvokeHandlerAsync(scope.ServiceProvider, tenant, clientsEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
    }

    private static async Task<Backend.Veteriner.Domain.Shared.Result<PagedResult<Backend.Veteriner.Application.Clients.Contracts.Dtos.ClientListItemDto>>> InvokeHandlerAsync(
        IServiceProvider sp,
        Guid tenantId,
        bool clientsEnabled)
    {
        var handler = new GetClientsListQueryHandler(
            new FixedTenantContext(tenantId),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IClientReadModelReader>(),
            Options.Create(new QueryReadModelsOptions { ClientsEnabled = clientsEnabled }));

        return await handler.Handle(new GetClientsListQuery(new PageRequest { Page = 1, PageSize = 50 }), CancellationToken.None);
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
