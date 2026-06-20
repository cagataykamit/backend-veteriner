using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Treatments.Queries.GetList;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Pets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Treatments;

/// <summary>
/// CQRS-12D-4 representative smoke: treatments list shared search lookup + tenant isolation.
/// </summary>
[Collection("pet-projection")]
public sealed class TreatmentSharedSearchLookupSmokeIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public TreatmentSharedSearchLookupSmokeIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task FlagOff_Should_FindTreatment_ByClientNameSearch_EvenWhenQueryDbEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seed = await SeedTreatmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task FlagOn_Should_NotFallback_WhenReadModelEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        await SeedTreatmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");

        var seed = await SeedTreatmentAsync(commandDb, clientFullName: "Mehmet Demir", petName: "Boncuk");

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task FlagOn_Should_FindTreatment_ByClientNameSearch_WhenReadModelPopulated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seed = await SeedTreatmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        await SeedPetReadModelAsync(queryDb, seed);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task FlagOn_Should_ReturnOnlyRowsForRequestedTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seedA = await SeedTreatmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        var seedB = await SeedTreatmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Other");
        await SeedPetReadModelAsync(queryDb, seedA);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seedA.TenantId,
            seedA.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seedA.TreatmentId);
        result.Value.Items.Should().NotContain(x => x.Id == seedB.TreatmentId);
    }

    private static async Task ResetQueryPetsAsync(QueryDbContext queryDb)
    {
        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await queryDb.PetReadModels.ExecuteDeleteAsync();
    }

    private static async Task<TreatmentSearchSeed> SeedTreatmentAsync(
        AppDbContext commandDb,
        string clientFullName,
        string petName)
    {
        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..20]);
        var clinic = new Clinic(tenant.Id, "Pilot Clinic", "Istanbul");
        commandDb.Tenants.Add(tenant);
        commandDb.Clinics.Add(clinic);
        await commandDb.SaveChangesAsync();

        var client = new Client(tenant.Id, clientFullName, "905551110088");
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();

        var speciesId = await commandDb.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var speciesName = await commandDb.Species.Where(s => s.Id == speciesId).Select(s => s.Name).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, petName, speciesId);
        commandDb.Pets.Add(pet);
        await commandDb.SaveChangesAsync();

        var treatment = new Treatment(
            tenant.Id,
            clinic.Id,
            pet.Id,
            examinationId: null,
            DateTime.UtcNow.AddHours(-2),
            "Antibiyotik",
            "Uygulama",
            null,
            null);
        commandDb.Treatments.Add(treatment);
        await commandDb.SaveChangesAsync();

        return new TreatmentSearchSeed(
            tenant.Id,
            clinic.Id,
            client.Id,
            pet.Id,
            treatment.Id,
            clientFullName,
            petName,
            speciesId,
            speciesName);
    }

    private static async Task SeedPetReadModelAsync(QueryDbContext queryDb, TreatmentSearchSeed seed)
    {
        var now = DateTime.UtcNow;
        queryDb.PetReadModels.Add(new PetReadModel
        {
            PetId = seed.PetId,
            TenantId = seed.TenantId,
            ClientId = seed.ClientId,
            ClientFullName = seed.ClientFullName,
            ClientFullNameNormalized = seed.ClientFullName.Trim().ToLowerInvariant(),
            Name = seed.PetName,
            NameNormalized = seed.PetName.Trim().ToLowerInvariant(),
            SpeciesId = seed.SpeciesId,
            SpeciesName = seed.SpeciesName,
            SpeciesNameNormalized = seed.SpeciesName.Trim().ToLowerInvariant(),
            LastEventId = Guid.NewGuid(),
            LastProjectedAtUtc = now,
            LastEventOccurredAtUtc = now
        });
        await queryDb.SaveChangesAsync();
    }

    private static async Task<Backend.Veteriner.Domain.Shared.Result<PagedResult<Backend.Veteriner.Application.Treatments.Contracts.Dtos.TreatmentListItemDto>>> InvokeHandlerAsync(
        IServiceProvider sp,
        Guid tenantId,
        Guid clinicId,
        string search,
        bool sharedSearchLookupEnabled)
    {
        var handler = new GetTreatmentsListQueryHandler(
            new FixedTenantContext(tenantId),
            new FixedClinicContext(clinicId),
            new PassthroughClinicReadScopeResolver(),
            sp.GetRequiredService<IReadRepository<Treatment>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IPetReadModelLookupReader>(),
            Options.Create(new QueryReadModelsOptions
            {
                SharedSearchLookupEnabled = sharedSearchLookupEnabled
            }));

        return await handler.Handle(
            new GetTreatmentsListQuery(
                new PageRequest { Page = 1, PageSize = 50, Search = search },
                clinicId),
            CancellationToken.None);
    }

    private sealed record TreatmentSearchSeed(
        Guid TenantId,
        Guid ClinicId,
        Guid ClientId,
        Guid PetId,
        Guid TreatmentId,
        string ClientFullName,
        string PetName,
        Guid SpeciesId,
        string SpeciesName);

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }

    private sealed class FixedClinicContext(Guid clinicId) : IClinicContext
    {
        public Guid? ClinicId { get; } = clinicId;
    }

    private sealed class PassthroughClinicReadScopeResolver : IClinicReadScopeResolver
    {
        public Task<Result<ClinicReadScope>> ResolveAsync(
            Guid tenantId,
            Guid? requestClinicId,
            CancellationToken ct)
            => Task.FromResult(Result<ClinicReadScope>.Success(new ClinicReadScope(requestClinicId, null)));
    }
}
